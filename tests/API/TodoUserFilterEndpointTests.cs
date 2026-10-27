using System.Net;
using System.Text;
using System.Text.Json;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Filtrování seznamu úkolů podle uživatele a stavu dokončení přes skutečné HTTP.
/// </summary>
public sealed class TodoUserFilterEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;

    public TodoUserFilterEndpointTests(ApiTestFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetAll_UserFilter_WorksWithCompletionFilterAndEdgeCases()
    {
        var aliceCompletedId = await CreateTodoAsync("alice", "Alice hotovo");
        var aliceOpenId = await CreateTodoAsync("alice", "Alice rozdělané");
        var bobOpenId = await CreateTodoAsync("bob", "Bob rozdělané");

        using var completed = await _client.PostAsync(
            $"/api/todos/{aliceCompletedId}/complete",
            null);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        // Bez filtrů -> všechny tři vytvořené úkoly.
        using var all = await GetTodosAsync("/api/todos");
        Assert.Equal(
            new[] { aliceCompletedId, aliceOpenId, bobOpenId }.Order(),
            GetIds(all).Order());

        // Pouze isCompleted -> všechny nedokončené úkoly.
        using var open = await GetTodosAsync("/api/todos?isCompleted=false");
        Assert.Equal(
            new[] { aliceOpenId, bobOpenId }.Order(),
            GetIds(open).Order());

        // Pouze userId -> všechny úkoly Alice.
        using var alice = await GetTodosAsync("/api/todos?userId=alice");
        Assert.Equal(
            new[] { aliceCompletedId, aliceOpenId }.Order(),
            GetIds(alice).Order());

        // userId + isCompleted -> pouze nedokončené úkoly Alice.
        using var aliceOpen = await GetTodosAsync(
            "/api/todos?userId=alice&isCompleted=false");

        Assert.Equal(
            new[] { aliceOpenId }.Order(),
            GetIds(aliceOpen).Order());

        // Neznámý uživatel -> 200 OK a prázdný seznam.
        using var unknown = await GetTodosAsync("/api/todos?userId=nobody");
        Assert.Empty(unknown.RootElement.EnumerateArray());

        // Porovnání userId je case-sensitive: alice != Alice.
        using var wrongCase = await GetTodosAsync("/api/todos?userId=Alice");
        Assert.Empty(wrongCase.RootElement.EnumerateArray());
    }

    private async Task<Guid> CreateTodoAsync(string userId, string title)
    {
        var body = JsonSerializer.Serialize(new { userId, title });

        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/todos")
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json")
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonDocument> GetTodosAsync(string url)
    {
        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
    }

    private static Guid[] GetIds(JsonDocument json) =>
        json.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
}