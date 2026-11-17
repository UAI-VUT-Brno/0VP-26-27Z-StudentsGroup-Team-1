using System.Net;
using System.Text;
using System.Text.Json;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Počet úkolů <c>GET /api/todos/count</c> přes skutečné HTTP: tvar odpovědi
/// a shoda filtrů se seznamem <c>GET /api/todos</c>.
/// </summary>
public sealed class TodoCountEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;

    public TodoCountEndpointTests(ApiTestFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetCount_NoFilters_ReturnsCountOfAllTasks()
    {
        await CreateTodoAsync(UniqueUserId("all"), "Úkol pro celkový počet");

        using var response = await _client.GetAsync("/api/todos/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var property = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("count", property.Name);
        Assert.Equal(JsonValueKind.Number, property.Value.ValueKind);
        Assert.True(property.Value.TryGetInt32(out var count), "Hodnota count není celé číslo.");

        // Bez filtrů odpovídá počet délce celého seznamu.
        Assert.Equal(await GetListLengthAsync("/api/todos"), count);
    }

    [Fact]
    public async Task GetCount_Filters_MatchTodoListEndpoint()
    {
        // Fixture sdílí úložiště s ostatními testy třídy – proto jedinečné userId.
        var alice = UniqueUserId("alice");
        var bob = UniqueUserId("bob");
        var aliceCompletedId = await CreateTodoAsync(alice, "Alice hotovo");
        await CreateTodoAsync(alice, "Alice rozdělané");
        await CreateTodoAsync(bob, "Bob rozdělané");

        using var completed = await _client.PostAsync($"/api/todos/{aliceCompletedId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        // Pouze userId -> oba úkoly Alice.
        await AssertCountMatchesListAsync($"userId={alice}", expected: 2);

        // userId + isCompleted -> jen rozdělaný úkol Alice.
        await AssertCountMatchesListAsync($"userId={alice}&isCompleted=false", expected: 1);
        await AssertCountMatchesListAsync($"userId={alice}&isCompleted=true", expected: 1);

        // Neznámý uživatel -> 0.
        await AssertCountMatchesListAsync($"userId={UniqueUserId("nobody")}", expected: 0);

        // Pouze isCompleted -> ostatní testy přidávají úkoly, ověří se tedy jen shoda se seznamem.
        await AssertCountMatchesListAsync("isCompleted=false", expected: null);
        await AssertCountMatchesListAsync("isCompleted=true", expected: null);
    }

    [Fact]
    public async Task GetCount_UserIdIsCaseSensitive()
    {
        var userId = UniqueUserId("carol");
        await CreateTodoAsync(userId, "Carol");

        Assert.Equal(1, await GetCountAsync($"/api/todos/count?userId={userId}"));
        Assert.Equal(0, await GetCountAsync($"/api/todos/count?userId={userId.ToUpperInvariant()}"));
    }

    [Fact]
    public async Task GetCount_InvalidIsCompleted_ReturnsBadRequest()
    {
        using var response = await _client.GetAsync("/api/todos/count?isCompleted=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Stejně jako u seznamu úkolů přijde ValidationProblemDetails s popisem chyb.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("errors").ValueKind);
    }

    /// <summary>Jedinečný identifikátor uživatele, aby se testy nepletly s úkoly jiných testů.</summary>
    private static string UniqueUserId(string name) => $"{name}-{Guid.NewGuid():N}";

    /// <summary>
    /// Ověří, že počet pro daný dotaz odpovídá délce seznamu se stejným dotazem
    /// a případně i očekávané hodnotě.
    /// </summary>
    private async Task AssertCountMatchesListAsync(string query, int? expected)
    {
        var count = await GetCountAsync($"/api/todos/count?{query}");
        var listLength = await GetListLengthAsync($"/api/todos?{query}");

        Assert.Equal(listLength, count);
        if (expected is not null)
        {
            Assert.Equal(expected.Value, count);
        }
    }

    private async Task<Guid> CreateTodoAsync(string userId, string title)
    {
        var body = JsonSerializer.Serialize(new { userId, title });

        using var response = await _client.PostAsync(
            "/api/todos",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<int> GetCountAsync(string url)
    {
        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("count").GetInt32();
    }

    private async Task<int> GetListLengthAsync(string url)
    {
        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.GetArrayLength();
    }
}
