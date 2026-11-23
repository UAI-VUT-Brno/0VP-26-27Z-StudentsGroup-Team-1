using System.Net;
using System.Text;
using System.Text.Json;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Verze úkolu (<c>version</c>) přes skutečné HTTP: vrací se v každé odpovědi, <c>PUT</c> a <c>DELETE</c>
/// ji musí poslat a zastaralá verze skončí odpovědí 409, aniž by se cokoli zapsalo.
/// </summary>
public sealed class TodoVersionEndpointTests : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Chyby z kontroleru (400, 404, 409) mají kvůli <c>[Produces("application/json")]</c> typ obsahu
    /// <c>application/json</c>; <c>application/problem+json</c> posílá jen obsluha výjimek (500). Viz docs/openapi.md.
    /// </summary>
    private const string ControllerProblemMediaType = "application/json";

    private readonly HttpClient _client;

    public TodoVersionEndpointTests(ApiTestFactory factory) => _client = factory.CreateClient();

    // AC 1, AC 5: Nový úkol má verzi 1 v odpovědi na založení, v detailu i v seznamu.
    [Fact]
    public async Task Post_NewTask_ReturnsVersionOneInEveryResponse()
    {
        using var created = await SendJsonAsync(HttpMethod.Post, "/api/todos", """{"userId":"jan.novak","title":"Úkol"}""");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("application/json", created.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJsonAsync(created);
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt64());

        var location = created.Headers.Location;
        Assert.NotNull(location);
        using var detail = await GetTodoAsync(location.AbsolutePath);
        Assert.Equal(1, detail.RootElement.GetProperty("version").GetInt64());

        var id = json.RootElement.GetProperty("id").GetGuid();
        using var list = JsonDocument.Parse(await _client.GetStringAsync("/api/todos"));
        var listed = list.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
        Assert.Equal(1, listed.GetProperty("version").GetInt64());
    }

    // AC 2: PUT se shodnou verzí vrátí 200 a verzi o 1 vyšší.
    [Fact]
    public async Task Put_MatchingVersion_Returns200AndIncrementsVersion()
    {
        var url = await CreateTodoAsync();

        using var updated = await PutAsync(url, "Nový název", version: 1);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("application/json", updated.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJsonAsync(updated);
        Assert.Equal("Nový název", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt64());
        await AssertStoredTaskAsync(url, "Nový název", version: 2);
    }

    // AC 3: PUT se zastaralou verzí vrátí 409 ProblemDetails a úkol se nezmění.
    [Fact]
    public async Task Put_StaleVersion_Returns409ProblemAndKeepsTask()
    {
        var url = await CreateTodoAsync("Původní");
        using var completed = await _client.PostAsync(url + "/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        using var stale = await PutAsync(url, "Zastaralý", version: 1);

        await AssertConflictProblemAsync(stale, currentVersion: 2);
        await AssertStoredTaskAsync(url, "Původní", version: 2);
    }

    // AC 3, AC 7: Dva PUT se stejnou původní verzí – první vyhraje, druhý dostane 409.
    [Fact]
    public async Task Put_SameVersionTwice_FirstWinsSecondReturns409()
    {
        var url = await CreateTodoAsync();

        using var first = await PutAsync(url, "První", version: 1);
        using var second = await PutAsync(url, "Druhý", version: 1);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertConflictProblemAsync(second, currentVersion: 2);
        await AssertStoredTaskAsync(url, "První", version: 2);
    }

    // AC 3: Chybějící nebo neplatná verze v těle PUT vrátí standardní 400 a úkol se nezmění.
    [Theory]
    [InlineData("""{"title":"Nový název"}""", "Version")]
    [InlineData("""{"title":"Nový název","version":null}""", "Version")]
    [InlineData("""{"title":"Nový název","version":0}""", "Version")]
    [InlineData("""{"title":"Nový název","version":-1}""", "Version")]
    [InlineData("""{"title":"Nový název","version":"1"}""", "$.version")]
    [InlineData("""{"title":"Nový název","version":1.5}""", "$.version")]
    public async Task Put_MissingOrInvalidVersion_Returns400AndKeepsTask(string body, string errorKey)
    {
        var url = await CreateTodoAsync("Původní");

        using var response = await SendJsonAsync(HttpMethod.Put, url, body);

        await AssertValidationProblemAsync(response, errorKey);
        await AssertStoredTaskAsync(url, "Původní", version: 1);
    }

    // AC 6: PUT s platnou verzí na neexistující úkol vrátí 404.
    [Fact]
    public async Task Put_UnknownIdWithValidVersion_Returns404()
    {
        using var response = await PutAsync($"/api/todos/{Guid.NewGuid()}", "Úkol", version: 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ControllerProblemMediaType, response.Content.Headers.ContentType?.MediaType);
    }

    // AC 2: DELETE se shodnou verzí vrátí 204 a úkol zmizí.
    [Fact]
    public async Task Delete_MatchingVersion_Returns204AndRemovesTask()
    {
        var url = await CreateTodoAsync();

        using var deleted = await _client.DeleteAsync(url + "?version=1");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await deleted.Content.ReadAsByteArrayAsync());
        using var detail = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
    }

    // AC 3: DELETE se zastaralou verzí vrátí 409 ProblemDetails a úkol zůstane.
    [Fact]
    public async Task Delete_StaleVersion_Returns409ProblemAndKeepsTask()
    {
        var url = await CreateTodoAsync("Původní");
        using var updated = await PutAsync(url, "Novější", version: 1);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var stale = await _client.DeleteAsync(url + "?version=1");

        await AssertConflictProblemAsync(stale, currentVersion: 2);
        await AssertStoredTaskAsync(url, "Novější", version: 2);
    }

    // AC 3: Chybějící nebo neplatný parametr version u DELETE vrátí standardní 400 a úkol zůstane.
    [Theory]
    [InlineData("")]
    [InlineData("?version=")]
    [InlineData("?version=0")]
    [InlineData("?version=-1")]
    [InlineData("?version=abc")]
    public async Task Delete_MissingOrInvalidVersion_Returns400AndKeepsTask(string query)
    {
        var url = await CreateTodoAsync("Původní");

        using var response = await _client.DeleteAsync(url + query);

        await AssertValidationProblemAsync(response, "version");
        await AssertStoredTaskAsync(url, "Původní", version: 1);
    }

    // AC 6: DELETE s platnou verzí na neexistující úkol vrátí 404.
    [Fact]
    public async Task Delete_UnknownIdWithValidVersion_Returns404()
    {
        using var response = await _client.DeleteAsync($"/api/todos/{Guid.NewGuid()}?version=1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ControllerProblemMediaType, response.Content.Headers.ContentType?.MediaType);
    }

    // AC 9: POST /complete zvýší verzi a odpověď ji obsahuje.
    [Fact]
    public async Task Complete_IncrementsVersion()
    {
        var url = await CreateTodoAsync();

        using var completed = await _client.PostAsync(url + "/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var json = await ReadJsonAsync(completed);
        Assert.True(json.RootElement.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt64());
        await AssertStoredTaskAsync(url, "Původní", version: 2);
    }

    // --- Pomocné metody ------------------------------------------------------

    /// <summary>Založí úkol (verze 1) a vrátí jeho adresu, např. <c>/api/todos/{id}</c>.</summary>
    private async Task<string> CreateTodoAsync(string title = "Původní")
    {
        using var created = await SendJsonAsync(
            HttpMethod.Post, "/api/todos", JsonSerializer.Serialize(new { userId = "jan.novak", title }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location;
        Assert.NotNull(location);
        return location.AbsolutePath;
    }

    private Task<HttpResponseMessage> PutAsync(string url, string title, long version) =>
        SendJsonAsync(HttpMethod.Put, url, JsonSerializer.Serialize(new { title, version }));

    private Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, string body) =>
        _client.SendAsync(new HttpRequestMessage(method, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<JsonDocument> GetTodoAsync(string url)
    {
        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>Ověří, že uložený úkol má očekávaný název i verzi – tedy že se nic nepřepsalo.</summary>
    private async Task AssertStoredTaskAsync(string url, string title, long version)
    {
        using var json = await GetTodoAsync(url);

        Assert.Equal(title, json.RootElement.GetProperty("title").GetString());
        Assert.Equal(version, json.RootElement.GetProperty("version").GetInt64());
    }

    /// <summary>Ověří odpověď 409 ve formátu ProblemDetails, jejíž <c>detail</c> uvádí aktuální verzi.</summary>
    private static async Task AssertConflictProblemAsync(HttpResponseMessage response, long currentVersion)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ControllerProblemMediaType, response.Content.Headers.ContentType?.MediaType);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
        Assert.Contains($"aktuální verzi {currentVersion},", json.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>Ověří standardní odpověď 400 s chybou u zadaného klíče.</summary>
    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string errorKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ControllerProblemMediaType, response.Content.Headers.ContentType?.MediaType);

        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(errorKey, out _), json.RootElement.ToString());
    }
}
