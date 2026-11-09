using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.API;

public sealed class McpServerIntegrationTests
{
    private const int TaskCount = 500;
    private const int ResponseTimeBudgetMs = 500;

    [Fact]
    public async Task ToolsCanBeDiscoveredAndCalledOverHttp()
    {
        using var settings = new ApiTestFactory();
        await using var server = CreateServer(settings, "Development");
        using var http = server.CreateClient();
        var seeded = await SeedTodosAsync(server.Services);
        await using var transport = CreateTransport(http);
        await using var client = await McpClient.CreateAsync(transport,
            new McpClientOptions { ProtocolVersion = "2025-11-25" });

        var tools = await client.ListToolsAsync();
        Assert.Equal(new[] { "get_todo", "list_todos" }, tools.Select(tool => tool.Name).Order());
        Assert.All(tools, tool =>
        {
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
        });

        var listSchema = tools.Single(tool => tool.Name == "list_todos").ProtocolTool.InputSchema;
        Assert.Equal(new[] { "isCompleted", "userId" }, PropertyNames(listSchema));
        Assert.True(!listSchema.TryGetProperty("required", out var required) || required.GetArrayLength() == 0);

        var getSchema = tools.Single(tool => tool.Name == "get_todo").ProtocolTool.InputSchema;
        Assert.Equal(new[] { "id" }, PropertyNames(getSchema));
        Assert.Equal("id", Assert.Single(getSchema.GetProperty("required").EnumerateArray()).GetString());

        await AssertListMatchesRestAsync(client, http, "", new Dictionary<string, object?>());
        await AssertListMatchesRestAsync(client, http, "?isCompleted=false",
            new Dictionary<string, object?> { ["isCompleted"] = false });
        await AssertListMatchesRestAsync(client, http, "?userId=user-1",
            new Dictionary<string, object?> { ["userId"] = "user-1" });
        await AssertListMatchesRestAsync(client, http, "?userId=user-1&isCompleted=true",
            new Dictionary<string, object?> { ["userId"] = "user-1", ["isCompleted"] = true });

        var id = seeded[0].Id;
        var result = await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = id.ToString() });
        var rest = await ReadRestJsonAsync(http, $"/api/todos/{id}");
        Assert.True(JsonElement.DeepEquals(rest, ReadToolJson(result)));

        await AssertLookupErrorAsync(client, Guid.Empty.ToString(), "No ToDo item");
        await AssertLookupErrorAsync(client, "invalid-guid", "valid GUID");

        var openApi = await ReadRestJsonAsync(http, "/openapi/v1.json");
        Assert.False(openApi.GetProperty("paths").TryGetProperty("/mcp", out _));
    }

    [Fact]
    public async Task ListReturnsFullDatasetWithinResponseTimeBudget()
    {
        using var settings = new ApiTestFactory();
        await using var server = CreateServer(settings, "Development");
        using var http = server.CreateClient();
        await SeedTodosAsync(server.Services);
        await using var transport = CreateTransport(http);
        await using var client = await McpClient.CreateAsync(transport);
        var expected = await ReadRestJsonAsync(http, "/api/todos");

        // Zahřátí zahrne první volání nástroje, ale do měření už se nepočítá.
        ReadToolJson(await client.CallToolAsync("list_todos"));

        var timer = Stopwatch.StartNew();
        var result = await client.CallToolAsync("list_todos");
        timer.Stop();

        var actual = ReadToolJson(result);
        Assert.Equal(TaskCount, actual.GetArrayLength());
        Assert.True(JsonElement.DeepEquals(expected, actual));
        Assert.True(timer.Elapsed.TotalMilliseconds <= ResponseTimeBudgetMs,
            $"list_todos took {timer.Elapsed.TotalMilliseconds:F1} ms (budget: {ResponseTimeBudgetMs} ms).");
    }

    [Theory]
    [InlineData("Development", "false")]
    [InlineData("Production", null)]
    public async Task DisabledEndpointReturnsNotFound(string environment, string? enabled)
    {
        using var settings = new ApiTestFactory();
        await using var server = CreateServer(settings, environment, enabled);
        using var http = server.CreateClient();
        using var response = await http.PostAsync("/mcp", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateServer(
        ApiTestFactory settings, string environment, string? enabled = null)
    {
        // Nastavení Kestrelu musí být na výsledné továrně, před prvním CreateClient.
        var server = settings.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (enabled is not null)
            {
                builder.UseSetting("Mcp:Enabled", enabled);
            }
        });
        server.UseKestrel(0);
        return server;
    }

    private static HttpClientTransport CreateTransport(HttpClient http) => new(new HttpClientTransportOptions
    {
        Endpoint = new Uri(http.BaseAddress!, "/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp
    }, http);

    private static async Task<IReadOnlyList<TodoItem>> SeedTodosAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITodoRepository>();
        var start = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var priorities = Enum.GetValues<TodoPriority>();

        return await repository.AddRangeAsync(Enumerable.Range(0, TaskCount).Select(i => new TodoItem
        {
            Id = new Guid(i + 1, 0, 0, new byte[8]),
            Title = $"Test task {i:D3}",
            UserId = $"user-{i % 5}",
            Priority = priorities[i % priorities.Length],
            IsCompleted = i % 2 == 0,
            CompletedAtUtc = i % 2 == 0 ? start.AddDays(1) : null,
            CreatedAtUtc = start.AddMinutes(i),
            UpdatedAtUtc = start.AddDays(1),
            DueAtUtc = i % 3 == 0 ? start.AddDays(i) : null
        }));
    }

    private static IEnumerable<string> PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order();

    private static async Task AssertListMatchesRestAsync(
        McpClient client, HttpClient http, string query, Dictionary<string, object?> arguments)
    {
        var expected = await ReadRestJsonAsync(http, "/api/todos" + query);
        var actual = ReadToolJson(await client.CallToolAsync("list_todos", arguments));

        Assert.True(JsonElement.DeepEquals(expected, actual), $"Unexpected list_todos result for '{query}'.");
    }

    private static async Task AssertLookupErrorAsync(McpClient client, string id, string message)
    {
        var result = await client.CallToolAsync("get_todo", new Dictionary<string, object?> { ["id"] = id });

        Assert.True(result.IsError);
        Assert.Contains(message, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    private static JsonElement ReadToolJson(CallToolResult result)
    {
        Assert.False(result.IsError ?? false);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> ReadRestJsonAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
