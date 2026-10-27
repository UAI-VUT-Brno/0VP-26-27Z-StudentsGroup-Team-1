using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.OpenApi;

/// <summary>
/// Dokument OpenAPI na <c>/openapi/v1.json</c>: je platný podle specifikace, popisuje všechny
/// endpointy aplikace a odpovídá tomu, co API skutečně vrací.
/// <para>
/// API běží uvnitř testu přes <c>WebApplicationFactory</c>. Dokument se čte knihovnou
/// Microsoft.OpenApi, tedy stejně, jako ho čtou generátory klientů.
/// </para>
/// </summary>
public sealed partial class OpenApiDocumentTests : IClassFixture<OpenApiTestFactory>
{
    /// <summary>Identifikátor, který v úložišti není (ukázková data mají náhodné GUID).</summary>
    private const string MissingId = "00000000-0000-0000-0000-000000000001";
    
    /// <summary>Platné tělo požadavku <c>POST /api/todos/repeat</c>.</summary>
    private const string RepeatBody = "{\"userId\":\"jan.novak\",\"title\":\"Týdenní report\",\"firstDueAtUtc\":\"2026-10-26T09:00:00Z\",\"repeatCount\":3}";

    private readonly OpenApiTestFactory _factory;
    private readonly HttpClient _client;

    public OpenApiDocumentTests(OpenApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- Platnost dokumentu --------------------------------------------------

    [Fact]
    public async Task Document_IsValidOpenApi31()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        var result = OpenApiDocument.Parse(json, OpenApiConstants.Json);

        Assert.Empty(result.Diagnostic!.Errors);
        Assert.Equal(OpenApiSpecVersion.OpenApi3_1, result.Diagnostic.SpecificationVersion);
        Assert.Empty(result.Document!.Validate(ValidationRuleSet.GetDefaultRuleSet())!);
    }

    [Fact]
    public async Task Document_IsAlsoServedAsYaml()
    {
        using var response = await _client.GetAsync("/openapi/v1.yaml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("openapi: '3.1", await response.Content.ReadAsStringAsync());
    }

    // --- Úplnost -------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/todos", "200 400 500")]
    [InlineData("POST", "/api/todos", "201 400 415 500")]
    [InlineData("POST", "/api/todos/repeat", "201 400 415 500")]
    [InlineData("GET", "/api/todos/{id}", "200 404 500")]
    [InlineData("PUT", "/api/todos/{id}", "200 400 404 415 500")]
    [InlineData("DELETE", "/api/todos/{id}", "204 404 500")]
    [InlineData("POST", "/api/todos/{id}/complete", "200 404 500")]
    [InlineData("GET", "/api/info", "200 500")]
    [InlineData("GET", "/health", "200 503")]
    public async Task Operation_DocumentsAllResponses(string method, string path, string statusCodes)
    {
        var operation = GetOperation(await LoadDocumentAsync(), method, path);

        Assert.False(string.IsNullOrWhiteSpace(operation.Summary));
        Assert.Equal(statusCodes.Split(' '), operation.Responses!.Keys);
        Assert.All(operation.Responses.Values, response => Assert.False(string.IsNullOrWhiteSpace(response.Description)));
    }

    [Fact]
    public async Task Document_DescribesEveryEndpointOfTheApplication()
    {
        var document = await LoadDocumentAsync();

        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                is not { ExcludeFromDescription: true })
            .ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            // "api/todos/{id:guid}" → "/api/todos/{id}"
            var path = "/" + RouteConstraint().Replace(endpoint.RoutePattern.RawText!.TrimStart('/'), "{$1}");
            Assert.True(document.Paths.ContainsKey(path), $"Endpoint {path} v dokumentu chybí.");

            // /health nemá omezení na metodu – stačí, že je jeho cesta zdokumentovaná.
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                Assert.True(
                    document.Paths[path].Operations!.ContainsKey(new HttpMethod(method)),
                    $"Operace {method} {path} v dokumentu chybí.");
            }
        }
    }

    // --- Typy a struktura ----------------------------------------------------

    [Fact]
    public async Task Priority_IsDocumentedAsTheStringsTheApiSends()
    {
        var schema = (await LoadDocumentAsync()).Components!.Schemas!["TodoPriority"];

        Assert.Equal(Enum.GetNames<TodoPriority>(), schema.Enum!.Select(value => value!.GetValue<string>()));
    }

    [Fact]
    public async Task Numbers_AreDocumentedAsIntegersOnly()
    {
        var schema = (await LoadDocumentAsync()).Components!.Schemas!["InstanceInfoDto"];

        Assert.Equal(JsonSchemaType.Integer, schema.Properties!["todoCount"].Type);
    }

    [Fact]
    public async Task DateWithCustomConverter_IsDocumentedAsDateTimeString()
    {
        // Vlastní převodník JSON schovává generátoru typ; doplňuje ho AssumeUtcDateTimeSchemaTransformer.
        var schema = (await LoadDocumentAsync()).Components!.Schemas!["CreateRepeatingTodoItemRequest"];

        var firstDue = schema.Properties!["firstDueAtUtc"];
        Assert.Equal(JsonSchemaType.String, firstDue.Type);
        Assert.Equal("date-time", firstDue.Format);
    }

    [Fact]
    public async Task CreatedResponse_DocumentsLocationHeader()
    {
        var response = GetOperation(await LoadDocumentAsync(), "POST", "/api/todos").Responses!["201"];

        Assert.True(response.Headers!.TryGetValue("Location", out var header));
        Assert.True(header.Required);
    }

    [Theory]
    [InlineData("GET", "/api/info", "200")]
    [InlineData("GET", "/api/todos/{id}", "404")]
    [InlineData("DELETE", "/api/todos/{id}", "204")]
    public async Task EveryResponse_DocumentsInstanceHeader(string method, string path, string statusCode)
    {
        var response = GetOperation(await LoadDocumentAsync(), method, path).Responses![statusCode];

        Assert.True(response.Headers!.TryGetValue("X-Instance-Id", out var header));
        Assert.True(header.Required);
    }


    [Fact]
    public async Task NoContentResponse_HasNoBody()
    {
        var response = GetOperation(await LoadDocumentAsync(), "DELETE", "/api/todos/{id}").Responses!["204"];

        Assert.True(response.Content is null or { Count: 0 });
    }

    [Fact]
    public async Task ErrorResponses_UseProblemDetails()
    {
        var document = await LoadDocumentAsync();

        var errorResponses = document.Paths
            .Where(path => path.Key.StartsWith("/api/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.Operations!.Values)
            .SelectMany(operation => operation.Responses!)
            .Where(response => int.Parse(response.Key, CultureInfo.InvariantCulture) >= 400)
            .ToList();

        Assert.NotEmpty(errorResponses);
        Assert.All(errorResponses, response => Assert.All(response.Value.Content!.Values, mediaType =>
            Assert.Contains(
                Assert.IsType<OpenApiSchemaReference>(mediaType.Schema).Reference.Id,
                new[] { "ProblemDetails", "ValidationProblemDetails" })));
    }

    /// <summary>
    /// Generátor XML komentářů v .NET 10 zapíše do popisu těla požadavku každý <c>&lt;param&gt;</c>,
    /// který nepatří k parametru operace, i <c>cancellationToken</c>. Vyhraje poslední z nich,
    /// proto je v kontroleru <c>&lt;param name="request"&gt;</c> až za <c>cancellationToken</c>.
    /// </summary>
    [Theory]
    [InlineData("POST", "/api/todos", "Údaje nového úkolu.")]
    [InlineData("PUT", "/api/todos/{id}", "Nové hodnoty úkolu.")]
    [InlineData("POST", "/api/todos/repeat", "Údaje opakovaného úkolu a počet opakování.")]
    public async Task RequestBody_IsDescribedByTheRequestParameter(string method, string path, string description)
    {
        var operation = GetOperation(await LoadDocumentAsync(), method, path);

        Assert.Equal(description, operation.RequestBody!.Description);
    }

    // --- Shoda se skutečnými odpověďmi ---------------------------------------

    [Theory]
    [InlineData("GET", "/api/todos", "/api/todos", null, null, 200)]
    [InlineData("GET", "/api/todos?isCompleted=abc", "/api/todos", null, null, 400)]
    [InlineData("POST", "/api/todos", "/api/todos", "{\"title\":\"\"}", "application/json", 400)]
    [InlineData("POST", "/api/todos", "/api/todos", "{neplatný json", "application/json", 400)]
    [InlineData("POST", "/api/todos", "/api/todos", "Úkol", "text/plain", 415)]
    [InlineData("POST", "/api/todos/repeat", "/api/todos/repeat", RepeatBody, "application/json", 201)]
    [InlineData("POST", "/api/todos/repeat", "/api/todos/repeat", "{\"title\":\"Úkol\",\"repeatCount\":0}", "application/json", 400)]
    [InlineData("POST", "/api/todos/repeat", "/api/todos/repeat", "Úkol", "text/plain", 415)]
    [InlineData("GET", "/api/todos/" + MissingId, "/api/todos/{id}", null, null, 404)]
    [InlineData("PUT", "/api/todos/" + MissingId, "/api/todos/{id}", "{\"title\":\"Úkol\"}", "application/json", 404)]
    [InlineData("PUT", "/api/todos/" + MissingId, "/api/todos/{id}", "Úkol", "text/plain", 415)]
    [InlineData("POST", "/api/todos/" + MissingId + "/complete", "/api/todos/{id}/complete", null, null, 404)]
    [InlineData("DELETE", "/api/todos/" + MissingId, "/api/todos/{id}", null, null, 404)]
    [InlineData("GET", "/api/info", "/api/info", null, null, 200)]
    [InlineData("GET", "/health", "/health", null, null, 200)]
    public async Task Response_MatchesDocument(
        string method, string url, string documentedPath, string? body, string? contentType, int expectedStatusCode)
    {
        var document = await LoadDocumentAsync();

        using var response = await SendAsync(_client, method, url, body, contentType);

        Assert.Equal(expectedStatusCode, (int)response.StatusCode);
        await AssertDocumentedAsync(document, method, documentedPath, response);
    }

    [Fact]
    public async Task SuccessfulTodoLifecycle_MatchesDocument()
    {
        var document = await LoadDocumentAsync();
        const string body = "{\"userId\":\"jan.novak\",\"title\":\"Napsat Dockerfile\",\"priority\":\"High\"}";

        using var created = await SendAsync(_client, "POST", "/api/todos", body, "application/json");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AssertDocumentedAsync(document, "POST", "/api/todos", created);
        var url = created.Headers.Location!.AbsolutePath;

        using var detail = await SendAsync(_client, "GET", url);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await AssertDocumentedAsync(document, "GET", "/api/todos/{id}", detail);

        using var updated = await SendAsync(_client, "PUT", url, body, "application/json");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await AssertDocumentedAsync(document, "PUT", "/api/todos/{id}", updated);

        using var completed = await SendAsync(_client, "POST", url + "/complete");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        await AssertDocumentedAsync(document, "POST", "/api/todos/{id}/complete", completed);

        using var deleted = await SendAsync(_client, "DELETE", url);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertDocumentedAsync(document, "DELETE", "/api/todos/{id}", deleted);
    }

    [Fact]
    public async Task UnhandledException_ReturnsDocumentedServerError()
    {
        var document = await LoadDocumentAsync();
        await using var failingFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ITodoRepository, FailingTodoRepository>()));
        using var client = failingFactory.CreateClient();

        using var response = await SendAsync(client, "GET", "/api/todos");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertDocumentedAsync(document, "GET", "/api/todos", response);
    }

    // --- Pomocné metody ------------------------------------------------------

    [GeneratedRegex(@"\{(\w+)(:[^}]*)?\}")]
    private static partial Regex RouteConstraint();

    private async Task<OpenApiDocument> LoadDocumentAsync()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        return OpenApiDocument.Parse(json, OpenApiConstants.Json).Document!;
    }

    private static OpenApiOperation GetOperation(OpenApiDocument document, string method, string path)
    {
        Assert.True(document.Paths.TryGetValue(path, out var pathItem), $"Cesta {path} v dokumentu chybí.");
        Assert.True(
            pathItem!.Operations!.TryGetValue(new HttpMethod(method), out var operation),
            $"Operace {method} {path} v dokumentu chybí.");

        return operation!;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, string url, string? body = null, string? contentType = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType!);
        }

        return client.SendAsync(request);
    }

    /// <summary>Ověří, že dokument uvádí status kód, typ obsahu i povinné hlavičky odpovědi.</summary>
    private static async Task AssertDocumentedAsync(
        OpenApiDocument document, string method, string path, HttpResponseMessage response)
    {
        var statusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var operation = GetOperation(document, method, path);

        Assert.True(
            operation.Responses!.TryGetValue(statusCode, out var documented),
            $"{method} {path} vrátil {statusCode}, dokument tuto odpověď neuvádí.");

        if (documented!.Content is not { Count: > 0 })
        {
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
        else
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            Assert.True(
                mediaType is not null && documented.Content.ContainsKey(mediaType),
                $"{method} {path} {statusCode} vrátil typ obsahu {mediaType}, "
                + $"dokument uvádí {string.Join(", ", documented.Content.Keys)}.");
        }

        foreach (var (name, header) in documented.Headers ?? new Dictionary<string, IOpenApiHeader>())
        {
            Assert.True(
                !header.Required || response.Headers.Contains(name),
                $"{method} {path} {statusCode} neposlal hlavičku {name}.");
        }
    }

    /// <summary>Úložiště, které při každém volání selže – pro ověření odpovědi 500.</summary>
    private sealed class FailingTodoRepository : ITodoRepository
    {
        private static InvalidOperationException Failure() => new("Simulovaná chyba úložiště.");

        public Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw Failure();

        public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default) => throw Failure();

        public Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default) => 
            throw Failure();

        public Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default) => throw Failure();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw Failure();

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw Failure();
    }
}
