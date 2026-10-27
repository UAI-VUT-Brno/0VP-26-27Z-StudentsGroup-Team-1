using System.Net;
using System.Text;
using System.Text.Json;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Uživatel úkolu (<c>userId</c>) přes skutečné HTTP: povinný při založení, vrací se ve všech
/// odpovědích a úplná aktualizace ho nezmění.
/// </summary>
public sealed class TodoUserIdEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;

    public TodoUserIdEndpointTests(ApiTestFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Post_WithUserId_ReturnsItTrimmed_AndGetEndpointsReturnItToo()
    {
        using var created = await SendJsonAsync(HttpMethod.Post, "/api/todos", """{"userId":"  jan.novak  ","title":"Úkol"}""");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ReadJsonAsync(created);
        Assert.Equal("jan.novak", json.RootElement.GetProperty("userId").GetString());

        using var detail = await ReadJsonAsync(await _client.GetAsync(created.Headers.Location));
        Assert.Equal("jan.novak", detail.RootElement.GetProperty("userId").GetString());

        var id = json.RootElement.GetProperty("id").GetGuid();
        using var list = JsonDocument.Parse(await _client.GetStringAsync("/api/todos"));
        var listed = list.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
        Assert.Equal("jan.novak", listed.GetProperty("userId").GetString());
    }

    [Theory]
    [InlineData("""{"title":"Úkol"}""")]
    [InlineData("""{"userId":"","title":"Úkol"}""")]
    [InlineData("""{"userId":"   ","title":"Úkol"}""")]
    [InlineData("""{"userId":null,"title":"Úkol"}""")]
    public async Task Post_InvalidUserId_Returns400AndCreatesNothing(string body)
    {
        var countBefore = await GetTodoCountAsync();

        using var response = await SendJsonAsync(HttpMethod.Post, "/api/todos", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("UserId", out _), json.RootElement.ToString());
        Assert.Equal(countBefore, await GetTodoCountAsync());
    }

    [Fact]
    public async Task Post_UserIdOver100Characters_Returns400()
    {
        var body = JsonSerializer.Serialize(new { userId = new string('u', 101), title = "Úkol" });

        using var response = await SendJsonAsync(HttpMethod.Post, "/api/todos", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_DoesNotChangeTheUser()
    {
        using var created = await SendJsonAsync(HttpMethod.Post, "/api/todos", """{"userId":"jan.novak","title":"Úkol"}""");
        var url = created.Headers.Location!;

        using var updated = await SendJsonAsync(HttpMethod.Put, url.ToString(), """{"userId":"někdo.jiný","title":"Nový název"}""");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var json = await ReadJsonAsync(updated);
        Assert.Equal("jan.novak", json.RootElement.GetProperty("userId").GetString());
        Assert.Equal("Nový název", json.RootElement.GetProperty("title").GetString());
    }

    private Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, string body) =>
        _client.SendAsync(new HttpRequestMessage(method, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<int> GetTodoCountAsync()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/api/info"));

        return json.RootElement.GetProperty("todoCount").GetInt32();
    }
}
