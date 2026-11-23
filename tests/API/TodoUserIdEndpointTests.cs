using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Vlastník úkolu (<c>userId</c>) přes skutečné HTTP. Na rozdíl od testů kontroleru tu běží
/// i deserializace JSON a automatická validace <c>[ApiController]</c>, takže je vidět,
/// co klient opravdu pošle a dostane zpět.
/// </summary>
public sealed class TodoUserIdEndpointTests : IClassFixture<ApiTestFactory>, IDisposable
{
    private const string TodosUrl = "/api/todos";
    private const string RepeatUrl = "/api/todos/repeat";

    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "uai-zerovp-json-tests", Guid.NewGuid().ToString("N"));

    public TodoUserIdEndpointTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // --- Založení -----------------------------------------------------------

    // AC 1, 5: POST /api/todos vrátí userId oříznutý a se zachovanou velikostí písmen.
    [Fact]
    public async Task PostTodo_WithUserId_Returns201WithTrimmedUserId()
    {
        using var response = await PostJsonAsync(TodosUrl, """{"title":"Úkol","userId":"  Jan.Novak  "}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJsonAsync(response);
        Assert.Equal("Jan.Novak", json.RootElement.GetProperty("userId").GetString());
    }

    // AC 1: každý výskyt opakovaného úkolu nese stejný userId.
    [Fact]
    public async Task PostRepeat_WithUserId_EveryOccurrenceCarriesUserId()
    {
        using var response = await PostJsonAsync(
            RepeatUrl,
            """{"title":"Týdenní report","userId":"jan.novak","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var occurrences = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(4, occurrences.Count);
        Assert.All(occurrences, occurrence =>
            Assert.Equal("jan.novak", occurrence.GetProperty("userId").GetString()));
    }

    public static TheoryData<string, string, string> InvalidUserIds()
    {
        // { adresa, část těla s userId (prázdná = pole chybí), klíč v "errors" odpovědi 400 }
        var data = new TheoryData<string, string, string>();
        foreach (var url in new[] { TodosUrl, RepeatUrl })
        {
            data.Add(url, "", "UserId");
            data.Add(url, ",\"userId\":null", "UserId");
            data.Add(url, ",\"userId\":\"\"", "UserId");
            data.Add(url, ",\"userId\":\"   \"", "UserId");
            data.Add(url, $",\"userId\":\"{new string('u', 101)}\"", "UserId");
            data.Add(url, ",\"userId\":123", "$.userId");
        }

        return data;
    }

    // AC 2: neplatný userId vrátí 400 s chybou u userId a nevytvoří žádný úkol.
    [Theory]
    [MemberData(nameof(InvalidUserIds))]
    public async Task Post_InvalidUserId_Returns400ValidationProblemAndCreatesNothing(
        string url, string userIdMember, string errorKey)
    {
        var countBefore = await GetTodoCountAsync();

        using var response = await PostJsonAsync(url, CreateBody(url, userIdMember));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(
            json.RootElement.GetProperty("errors").TryGetProperty(errorKey, out _),
            $"Odpověď neobsahuje chybu pro {errorKey}: {json.RootElement}");
        Assert.Equal(countBefore, await GetTodoCountAsync());
    }

    // AC 5: userId o délce přesně 100 znaků projde.
    [Fact]
    public async Task Post_UserIdAtLimit_Returns201()
    {
        var userId = new string('u', 100);

        using var response = await PostJsonAsync(TodosUrl, CreateBody(TodosUrl, $",\"userId\":\"{userId}\""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(userId, json.RootElement.GetProperty("userId").GetString());
    }

    // --- Čtení --------------------------------------------------------------

    // AC 3: GET /api/todos/{id} vrací userId.
    [Fact]
    public async Task GetById_ReturnsUserId()
    {
        var id = await CreateTodoAsync("jan.novak");

        using var response = await _client.GetAsync($"{TodosUrl}/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal("jan.novak", json.RootElement.GetProperty("userId").GetString());
    }

    // AC 3: GET /api/todos vrací userId u každého úkolu.
    [Fact]
    public async Task GetAll_ReturnsUserId()
    {
        var id = await CreateTodoAsync("petra.svobodova");

        using var response = await _client.GetAsync(TodosUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.EnumerateArray().ToList();
        var item = Assert.Single(items, element => element.GetProperty("id").GetGuid() == id);
        Assert.Equal("petra.svobodova", item.GetProperty("userId").GetString());
        Assert.All(items, element => Assert.True(element.TryGetProperty("userId", out _)));
    }

    // --- Úprava -------------------------------------------------------------

    // AC 6: userId v těle PUT se ignoruje a vlastník zůstane původní.
    [Fact]
    public async Task Put_WithDifferentUserId_KeepsOriginalOwner()
    {
        var id = await CreateTodoAsync("jan.novak");

        using var updated = await _client.PutAsync(
            $"{TodosUrl}/{id}",
            new StringContent("""{"title":"Nový název","userId":"petra.svobodova"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedJson = await ReadJsonAsync(updated);
        Assert.Equal("Nový název", updatedJson.RootElement.GetProperty("title").GetString());
        Assert.Equal("jan.novak", updatedJson.RootElement.GetProperty("userId").GetString());

        using var detail = await _client.GetAsync($"{TodosUrl}/{id}");
        using var detailJson = await ReadJsonAsync(detail);
        Assert.Equal("jan.novak", detailJson.RootElement.GetProperty("userId").GetString());
    }

    // --- Soubor ze starší verze ---------------------------------------------

    // AC 3, 7: úkol ze souboru bez userId API vrátí se "userId": null.
    [Fact]
    public async Task Get_TaskFromFileWithoutUserId_ReturnsNullUserId()
    {
        const string legacyId = "0f8fad5b-d9cb-469f-a165-70867728950e";
        Directory.CreateDirectory(_directory);
        var filePath = Path.Combine(_directory, "todos.json");
        await File.WriteAllTextAsync(filePath, """
            [
              {
                "id": "0f8fad5b-d9cb-469f-a165-70867728950e",
                "title": "Úkol ze starší verze",
                "description": null,
                "priority": "Normal",
                "isCompleted": false,
                "dueAtUtc": null,
                "createdAtUtc": "2026-10-25T12:00:00+00:00",
                "completedAtUtc": null,
                "updatedAtUtc": "2026-10-25T12:00:00+00:00"
              }
            ]
            """);
        await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Todo:Storage:FilePath", filePath));
        using var client = factory.CreateClient();

        using var detail = await client.GetAsync($"{TodosUrl}/{legacyId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = await ReadJsonAsync(detail);
        Assert.Equal(JsonValueKind.Null, detailJson.RootElement.GetProperty("userId").ValueKind);

        using var list = await client.GetAsync(TodosUrl);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = await ReadJsonAsync(list);
        var item = Assert.Single(listJson.RootElement.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("userId").ValueKind);
    }

    // --- Pomocné metody -----------------------------------------------------

    /// <summary>Tělo požadavku pro danou adresu; <paramref name="userIdMember"/> se vloží za název.</summary>
    private static string CreateBody(string url, string userIdMember) =>
        string.Equals(url, RepeatUrl, StringComparison.Ordinal)
            ? $$"""{"title":"Úkol"{{userIdMember}},"firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}"""
            : $$"""{"title":"Úkol"{{userIdMember}}}""";

    /// <summary>Založí úkol se zadaným vlastníkem a vrátí jeho identifikátor.</summary>
    private async Task<Guid> CreateTodoAsync(string userId)
    {
        using var response = await PostJsonAsync(TodosUrl, CreateBody(TodosUrl, $",\"userId\":\"{userId}\""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PostJsonAsync(string url, string body) =>
        _client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<int> GetTodoCountAsync()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/api/info"));

        return json.RootElement.GetProperty("todoCount").GetInt32();
    }
}
