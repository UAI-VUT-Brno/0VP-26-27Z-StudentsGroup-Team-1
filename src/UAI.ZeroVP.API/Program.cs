using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using UAI.ZeroVP.API.OpenApi;
using UAI.ZeroVP.API.Instance;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.JsonFile;
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

// Jméno této instance – při běhu několika instancí podle něj poznáme, která co obsloužila.
var instance = InstanceIdentity.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(instance);

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        // Jméno instance v každém řádku logu.
        .Enrich.WithProperty("InstanceName", instance.Name)
        // Zápis do konzole a souboru běží na pozadí – požadavek nečeká na disk ani na terminál.
        .WriteTo.Async(sinks =>
        {
            sinks.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{InstanceName}] {Message:lj}{NewLine}{Exception}");
            sinks.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                shared: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{InstanceName}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
            
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
// S vyplněnou cestou Todo:Storage:FilePath se úkoly čtou a ukládají do souboru JSON při každé
// operaci, takže ho může sdílet víc instancí API; prázdná cesta = data jen v paměti (např. v testech).
var configuredTodoFilePath = builder.Configuration[$"{JsonFileTodoOptions.SectionName}:FilePath"];

if (string.IsNullOrWhiteSpace(configuredTodoFilePath))
{
    builder.Services.AddInMemoryTodoData(options =>
        builder.Configuration.GetSection(InMemoryTodoOptions.SectionName).Bind(options));
}
else
{
    // Relativní cesta se počítá od kořene aplikace, stejně jako u logů.
    var todoFilePath = Path.IsPathRooted(configuredTodoFilePath)
        ? configuredTodoFilePath
        : Path.Combine(builder.Environment.ContentRootPath, configuredTodoFilePath);

    builder.Services.AddJsonFileTodoData(
        storage => storage.FilePath = todoFilePath,
        options => builder.Configuration.GetSection(InMemoryTodoOptions.SectionName).Bind(options));
}

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
            Description = "Ukázkové API pro výuku Dockeru. Úkoly ukládá do souboru JSON, "
                        + "který může sdílet několik běžících instancí API."
        };

        return Task.CompletedTask;
    });
    
    // Co generátor z kódu nevyčte: endpoint /health, hlavička Location, odpovědi bez těla,
    // typ data s vlastním převodníkem JSON.
    options.AddDocumentTransformer<HealthCheckDocumentTransformer>();
    options.AddOperationTransformer<ResponsesOperationTransformer>();
    options.AddSchemaTransformer<AssumeUtcDateTimeSchemaTransformer>();
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
// Každá odpověď nese jméno instance v hlavičce X-Instance-Id. Nastavuje se až těsně před
// odesláním (OnStarting), protože obsluha výjimek hlavičky odpovědi maže – i chyba 500
// tak ukáže, která instance selhala.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers[InstanceIdentity.HeaderName] = instance.Name;
        return Task.CompletedTask;
    });

    return next(context);
});

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
    // Strojově čitelný popis API: /openapi/v1.json a stejný dokument ve formátu YAML.
    app.MapOpenApi();
    app.MapOpenApi("/openapi/{documentName}.yaml");

    // Interaktivní dokumentace nad tímto popisem – http://localhost:5258/swagger
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "UAI.ZeroVP – ToDo API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "UAI.ZeroVP – ToDo API";

        // Formulář pro odeslání požadavku je otevřený hned, bez klikání na "Try it out".
        options.EnableTryItOutByDefault();
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

