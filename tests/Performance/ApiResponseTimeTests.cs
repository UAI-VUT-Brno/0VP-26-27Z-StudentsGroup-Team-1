using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UAI.ZeroVP.DTO;
using Xunit.Abstractions;

namespace UAI.ZeroVP.Tests.Performance;

/// <summary>
/// Zátěžové testy limitu odezvy: žádný požadavek nesmí trvat déle než 500 ms,
/// ani když API současně používá více uživatelů.
/// <para>
/// API běží uvnitř testu přes <see cref="WebApplicationFactory{TEntryPoint}"/>. Měří se celý
/// pipeline ASP.NET Core (routing, validace, kontroler, serializace, logování), jen bez sítě.
/// </para>
/// </summary>
[Collection(PerformanceCollection.Name)]
[Trait("Category", "Performance")]
public sealed class ApiResponseTimeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const double ResponseTimeBudgetMs = 500;
    private const int ConcurrentUsers = 50;
    private const int IterationsPerUser = 20;

    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public ApiResponseTimeTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _client = factory.CreateClient();
        _output = output;
    }

    [Fact]
    public async Task ReadEndpoints_UnderConcurrentLoad_StayWithinBudget()
    {
        var id = await GetAnyTodoIdAsync();

        var samples = await RunWithWarmUpAsync(client => ReadScenarioAsync(client, id));

        AssertWithinBudget(samples);
    }

    [Fact]
    public async Task WriteEndpoints_UnderConcurrentLoad_StayWithinBudget()
    {
        var samples = await RunWithWarmUpAsync(WriteScenarioAsync);

        AssertWithinBudget(samples);
    }

    // --- Scénáře jednoho uživatele -------------------------------------------

    private static async Task ReadScenarioAsync(TimedHttpClient client, Guid id)
    {
        await client.SendAsync("GET /api/todos", HttpMethod.Get, "/api/todos");
        await client.SendAsync("GET /api/todos?isCompleted=false", HttpMethod.Get, "/api/todos?isCompleted=false");
        await client.SendAsync("GET /api/todos/{id}", HttpMethod.Get, $"/api/todos/{id}");
        await client.SendAsync("GET /api/info", HttpMethod.Get, "/api/info");
        await client.SendAsync("GET /health", HttpMethod.Get, "/health");
    }

    private static async Task WriteScenarioAsync(TimedHttpClient client)
    {
        var created = await client.SendAsync("POST /api/todos", HttpMethod.Post, "/api/todos",
            new CreateTodoItemRequest { Title = "Zátěžový test", Priority = TodoPriority.High });
        var id = JsonSerializer.Deserialize<TodoItemDto>(created, TimedHttpClient.Json)!.Id;

        await client.SendAsync("PUT /api/todos/{id}", HttpMethod.Put, $"/api/todos/{id}",
            new UpdateTodoItemRequest { Title = "Zátěžový test – upraveno", Priority = TodoPriority.Low });
        await client.SendAsync("POST /api/todos/{id}/complete", HttpMethod.Post, $"/api/todos/{id}/complete");
        await client.SendAsync("DELETE /api/todos/{id}", HttpMethod.Delete, $"/api/todos/{id}");
    }

    // --- Pomocné metody ------------------------------------------------------

    /// <summary>
    /// Scénář nejdřív jednou proběhne „naprázdno“: první volání každého endpointu
    /// zahrnuje JIT kompilaci a inicializaci MVC, což není běžná odezva API.
    /// </summary>
    private async Task<IReadOnlyList<RequestSample>> RunWithWarmUpAsync(Func<TimedHttpClient, Task> scenario)
    {
        await LoadRunner.RunAsync(_client, users: 1, iterationsPerUser: 1, scenario);

        return await LoadRunner.RunAsync(_client, ConcurrentUsers, IterationsPerUser, scenario);
    }

    private async Task<Guid> GetAnyTodoIdAsync()
    {
        var json = await _client.GetStringAsync("/api/todos");
        var items = JsonSerializer.Deserialize<List<TodoItemDto>>(json, TimedHttpClient.Json)!;

        // API se spouští s ukázkovými daty (Todo:SeedSampleData = true), seznam tedy není prázdný.
        Assert.NotEmpty(items);
        return items[0].Id;
    }

    private void AssertWithinBudget(IReadOnlyList<RequestSample> samples)
    {
        var statistics = EndpointStatistics.From(samples);

        _output.WriteLine($"{ConcurrentUsers} souběžných uživatelů × {IterationsPerUser} opakování, limit {ResponseTimeBudgetMs} ms");
        foreach (var endpoint in statistics)
        {
            _output.WriteLine(endpoint.ToString());
        }

        Assert.All(samples, sample =>
            Assert.True((int)sample.StatusCode < 400, $"{sample.Endpoint} vrátil chybu {(int)sample.StatusCode}."));

        Assert.All(statistics, endpoint =>
            Assert.True(endpoint.MaxMs <= ResponseTimeBudgetMs,
                $"{endpoint.Endpoint}: nejpomalejší odpověď {endpoint.MaxMs:F1} ms překročila limit {ResponseTimeBudgetMs} ms."));
    }
}