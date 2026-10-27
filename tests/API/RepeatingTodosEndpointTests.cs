using System.Net;
using System.Text;
using System.Text.Json;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// <c>POST /api/todos/repeat</c> přes skutečné HTTP. Na rozdíl od testů kontroleru tu běží
/// i deserializace JSON a automatická validace <c>[ApiController]</c> – právě ta musí
/// neplatný požadavek odmítnout dřív, než vznikne jakýkoli úkol.
/// </summary>
public sealed class RepeatingTodosEndpointTests : IClassFixture<ApiTestFactory>
{
    private const string ValidBody =
        """{"userId":"jan.novak","title":"Týdenní report","priority":"High","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""";

    private readonly HttpClient _client;

    public RepeatingTodosEndpointTests(ApiTestFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Post_ValidRequest_Returns201WithEveryOccurrence()
    {
        using var response = await PostJsonAsync("/api/todos/repeat", ValidBody);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var occurrences = json.RootElement.EnumerateArray().ToList();

        Assert.Equal(
            ["2026-10-26T09:00:00+00:00", "2026-11-02T09:00:00+00:00", "2026-11-09T09:00:00+00:00", "2026-11-16T09:00:00+00:00"],
            occurrences.Select(occurrence => occurrence.GetProperty("dueAtUtc").GetString()));
        Assert.Equal(4, occurrences.Select(occurrence => occurrence.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.All(occurrences, occurrence =>
        {
            Assert.Equal("jan.novak", occurrence.GetProperty("userId").GetString());
            Assert.Equal("Týdenní report", occurrence.GetProperty("title").GetString());
            Assert.Equal("High", occurrence.GetProperty("priority").GetString());
            Assert.False(occurrence.GetProperty("isCompleted").GetBoolean());
        });
    }

    [Fact]
    public async Task Post_ValidRequest_LocationLeadsToFirstOccurrence()
    {
        using var response = await PostJsonAsync("/api/todos/repeat", ValidBody);
        using var json = await ReadJsonAsync(response);
        var firstId = json.RootElement[0].GetProperty("id").GetGuid();

        Assert.Equal($"/api/todos/{firstId}", response.Headers.Location!.AbsolutePath);

        using var detail = await _client.GetAsync(response.Headers.Location);
        using var detailJson = await ReadJsonAsync(detail);
        Assert.Equal("2026-10-26T09:00:00+00:00", detailJson.RootElement.GetProperty("dueAtUtc").GetString());
    }

    [Fact]
    public async Task Post_ValidRequest_StoresEveryOccurrence()
    {
        var countBefore = await GetTodoCountAsync();

        using var response = await PostJsonAsync("/api/todos/repeat", ValidBody);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(countBefore + 4, await GetTodoCountAsync());
    }

    public static TheoryData<string, string> InvalidRequests => new()
    {
        // { tělo požadavku, klíč v "errors" odpovědi 400 }
        { """{"firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""",                       "Title" },
        { """{"userId":"jan.novak","title":"","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""",            "Title" },
        { """{"title":"Úkol","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""",                        "UserId" },
        { """{"userId":" ","title":"Úkol","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""",           "UserId" },
        { """{"userId":"jan.novak","title":"Úkol","priority":"Urgent","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}""", "$.priority" },
        { """{"userId":"jan.novak","title":"Úkol","repeatCount":3}""",                                               "FirstDueAtUtc" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":null,"repeatCount":3}""",                          "FirstDueAtUtc" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":"zítra","repeatCount":3}""",                       "$.firstDueAtUtc" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":"9999-12-30T00:00:00Z","repeatCount":3}""",        "FirstDueAtUtc" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":"2026-10-26T09:00:00Z"}""",                        "RepeatCount" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":0}""",        "RepeatCount" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":53}""",       "RepeatCount" },
        { """{"userId":"jan.novak","title":"Úkol","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":"3"}""",      "$.repeatCount" }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Post_InvalidRequest_Returns400ValidationProblemAndCreatesNoTasks(string body, string errorKey)
    {
        var countBefore = await GetTodoCountAsync();

        using var response = await PostJsonAsync("/api/todos/repeat", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(
            json.RootElement.GetProperty("errors").TryGetProperty(errorKey, out _),
            $"Odpověď neobsahuje chybu pro {errorKey}: {json.RootElement}");
        Assert.Equal(countBefore, await GetTodoCountAsync());
    }

    [Fact]
    public async Task PostTodo_WithoutDueDate_ReturnsDueAtUtcNull()
    {
        // Běžné založení úkolu zůstává kompatibilní; nové pole se posílá vždy, i s null.
        using var response = await PostJsonAsync("/api/todos", """{"userId":"jan.novak","title":"Úkol"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("dueAtUtc").ValueKind);
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