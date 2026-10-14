using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using UAI.ZeroVP.API.OpenApi;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.InMemory;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

var configuredLogFilePath =
    builder.Configuration["PersistentLogging:FilePath"]
    ?? "logs/uai-zerovp-.log";

var logFilePath = Path.IsPathRooted(configuredLogFilePath)
    ? configuredLogFilePath
    : Path.Combine(builder.Environment.ContentRootPath, configuredLogFilePath);

// Limit doby odezvy API. Pomalejší požadavky se v logu zvýrazní jako Warning.
var responseTimeBudgetMs = builder.Configuration.GetValue("Performance:ResponseTimeBudgetMs", 500.0);

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        // Zápis do konzole a souboru běží na pozadí – požadavek nečeká na disk ani na terminál.
        .WriteTo.Async(sinks =>
        {
            sinks.Console();
            sinks.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        });
});

// --- Služby ---------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));

// Generátor OpenAPI nečte nastavení kontrolerů výše, ale nastavení minimal API.
// Bez tohoto řádku by dokument popisoval výčty jako čísla, i když je API posílá jako řetězce.
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

// Jednotné chybové odpovědi ve formátu ProblemDetails (RFC 9457).
builder.Services.AddProblemDetails();

// Zdroj času jako služba – kontrolery i repozitář si ho berou z DI.
builder.Services.AddSingleton(TimeProvider.System);

// Datová vrstva: paměťové úložiště. Nastavení se čte ze sekce "Todo",
// v kontejneru ho lze přepsat proměnnou prostředí Todo__SeedSampleData.
builder.Services.AddInMemoryTodoData(options =>
    builder.Configuration.GetSection(InMemoryTodoOptions.SectionName).Bind(options));

// Kontrola zdraví aplikace – později ji využije HEALTHCHECK v Dockerfile.
builder.Services.AddHealthChecks();

// Popis API ve formátu OpenAPI. Dokument generuje vestavěná knihovna .NET 10
// (Microsoft.AspNetCore.OpenApi) a vystavuje ho na /openapi/v1.json.
// Swagger UI je pak jen prohlížeč tohoto dokumentu – žádný druhý generátor.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "UAI.ZeroVP – ToDo API",
            Version = "v1",
            Description = "Ukázkové API pro výuku Dockeru. Data jsou uložena pouze "
                        + "v paměti procesu a po zastavení kontejneru se ztratí."
        };

        return Task.CompletedTask;
    });

    // Co generátor z kódu nevyčte: endpoint /health, hlavička Location, odpovědi bez těla.
    options.AddDocumentTransformer<HealthCheckDocumentTransformer>();
    options.AddOperationTransformer<ResponsesOperationTransformer>();
});

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation(
        "Application lifecycle event {Event} completed with result {Result}.",
        "Start",
        "Success"));

app.Lifetime.ApplicationStopped.Register(() =>
    app.Logger.LogInformation(
        "Application lifecycle event {Event} completed with result {Result}.",
        "Stop",
        "Success"));

// Swagger UI je ve vývoji zapnutý; v kontejneru (prostředí Production) se dá
// zapnout proměnnou prostředí Swagger__Enabled=true.
var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment());

// --- HTTP pipeline --------------------------------------------------------

// Bez přesměrování na HTTPS: v kontejneru se vystavuje pouze HTTP port
// a TLS řeší až reverzní proxy před ním.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsedMs, exception) =>
    {
        if (exception is not null || httpContext.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        if (httpContext.Response.StatusCode >= 400)
        {
            return LogEventLevel.Warning;
        }

        // Úspěšný, ale pomalý požadavek – i zdravotní kontrola, pomalá /health je také problém.
        if (elapsedMs > responseTimeBudgetMs)
        {
            return LogEventLevel.Warning;
        }

        if (httpContext.Request.Path.StartsWithSegments("/health"))
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    };
});

if (swaggerEnabled)
{
    // Strojově čitelný popis API.
    app.MapOpenApi();

    // Interaktivní dokumentace nad tímto popisem – http://localhost:5258/swagger
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "UAI.ZeroVP – ToDo API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "UAI.ZeroVP – ToDo API";
    });
}

app.MapHealthChecks("/health");
app.MapControllers();

// Kořen vede na Swagger, ať má "docker run" hned co ukázat.
// Když je Swagger vypnutý, přesměruje rovnou na seznam úkolů.
app.MapGet("/", () => Results.Redirect(swaggerEnabled ? "/swagger" : "/api/todos"))
   .ExcludeFromDescription();

app.Run();

// Jedno nastavení JSON pro kontrolery i pro dokument OpenAPI, aby se nerozcházely.
static void ConfigureJson(JsonSerializerOptions options)
{
    // Výčty posílá API jako řetězce ("High"), ne jako čísla – čitelnější v curl i ve Swaggeru.
    options.Converters.Add(new JsonStringEnumConverter());

    // Čísla pouze jako JSON čísla. Výchozí webové nastavení přijímá i "5" v uvozovkách
    // a dokument by pak u každého čísla uváděl typ "integer nebo string".
    options.NumberHandling = JsonNumberHandling.Strict;
}
