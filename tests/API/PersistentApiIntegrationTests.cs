using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Integrační testy skutečného API nad dočasným JSON souborem.
/// Ověřují chování, které vzniká až spojením API, persistence a více instancí.
/// </summary>
public sealed class PersistentApiIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "uai-zerovp-persistent-api-tests",
        Guid.NewGuid().ToString("N"));

    private string StorageFilePath => Path.Combine(_directory, "todos.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTodo_NewHostUsingSameFile_LoadsPersistedTodo()
    {
        Guid createdId;

        await using (var firstFactory = CreateFactory("api-a"))
        using (var firstClient = firstFactory.CreateClient())
        {
            createdId = await CreateTodoAsync(
                firstClient,
                userId: "alice",
                title: "Persistent task");
        }

        await using var secondFactory = CreateFactory("api-b");
        using var secondClient = secondFactory.CreateClient();

        using var response = await secondClient.GetAsync("/api/todos");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var todo = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(createdId, todo.GetProperty("id").GetGuid());
        Assert.Equal("alice", todo.GetProperty("userId").GetString());
        Assert.Equal("Persistent task", todo.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CorruptedFile_ApiStartsWithEmptyStore_AndLogsDiagnostic()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StorageFilePath, "{ this is not valid JSON");

        await using var factory = CreateFactory("api-corrupted");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/todos");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Empty(document.RootElement.EnumerateArray());

        Assert.Contains(
            factory.StoreLogger.Collector.GetSnapshot(),
            record =>
                record.Level >= LogLevel.Warning
                && record.Message.Contains(
                    StorageFilePath,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task TwoHostsUsingSameFile_TodoCreatedThroughOne_IsVisibleThroughOther()
    {
        await using var firstFactory = CreateFactory("api-a");
        await using var secondFactory = CreateFactory("api-b");

        using var firstClient = firstFactory.CreateClient();
        using var secondClient = secondFactory.CreateClient();

        var createdId = await CreateTodoAsync(
            firstClient,
            userId: "alice",
            title: "Shared task");

        using var response = await secondClient.GetAsync("/api/todos");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var todo = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(createdId, todo.GetProperty("id").GetGuid());
        Assert.Equal("alice", todo.GetProperty("userId").GetString());
        Assert.Equal("Shared task", todo.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PersistentStore_UserFilter_WorksWithCompletionFilter()
    {
        await using var factory = CreateFactory("api-filter");
        using var client = factory.CreateClient();

        var aliceCompletedId = await CreateTodoAsync(
            client,
            userId: "alice",
            title: "Alice completed");

        var aliceOpenId = await CreateTodoAsync(
            client,
            userId: "alice",
            title: "Alice open");

        await CreateTodoAsync(
            client,
            userId: "bob",
            title: "Bob open");

        using var completedResponse = await client.PostAsync(
            $"/api/todos/{aliceCompletedId}/complete",
            null);

        completedResponse.EnsureSuccessStatusCode();

        var aliceIds = await GetTodoIdsAsync(
            client,
            "/api/todos?userId=alice");

        Assert.Equal(
            new[] { aliceCompletedId, aliceOpenId }.Order(),
            aliceIds.Order());

        var aliceOpenIds = await GetTodoIdsAsync(
            client,
            "/api/todos?userId=alice&isCompleted=false");

        Assert.Equal(
            new[] { aliceOpenId },
            aliceOpenIds);
    }

    private PersistentApiTestFactory CreateFactory(string instanceName) =>
        new(StorageFilePath, instanceName);

    private static async Task<Guid> CreateTodoAsync(
        HttpClient client,
        string userId,
        string title)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/todos",
            new
            {
                UserId = userId,
                Title = title,
            });

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid[]> GetTodoIdsAsync(
        HttpClient client,
        string url)
    {
        using var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
    }
}
