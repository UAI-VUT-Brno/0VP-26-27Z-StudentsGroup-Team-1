using System.Net;
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

    // AC 4, AC 2, AC 3: Úspěšný zápis přes jednu instanci zneplatní verzi, kterou drží druhá.
    [Fact]
    public async Task TwoHostsUsingSameFile_UpdateThroughOne_MakesVersionStaleOnOther()
    {
        await using var firstFactory = CreateFactory("api-a");
        await using var secondFactory = CreateFactory("api-b");

        using var firstClient = firstFactory.CreateClient();
        using var secondClient = secondFactory.CreateClient();

        var id = await CreateTodoAsync(
            firstClient,
            userId: "alice",
            title: "Shared task");

        using (var readByB = await GetTodoAsync(secondClient, id))
        {
            Assert.Equal(1, readByB.RootElement.GetProperty("version").GetInt64());
        }

        using (var updatedByA = await PutTodoAsync(firstClient, id, "Changed by A", version: 1))
        {
            Assert.Equal(HttpStatusCode.OK, updatedByA.StatusCode);
            using var json = await ReadJsonAsync(updatedByA);
            Assert.Equal(2, json.RootElement.GetProperty("version").GetInt64());
        }

        using (var staleFromB = await PutTodoAsync(secondClient, id, "Changed by B", version: 1))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleFromB.StatusCode);
            Assert.Equal(
                "application/problem+json",
                staleFromB.Content.Headers.ContentType?.MediaType);

            using var problem = await ReadJsonAsync(staleFromB);
            Assert.Contains(
                "aktuální verzi 2,",
                problem.RootElement.GetProperty("detail").GetString());
        }

        using (var current = await GetTodoAsync(secondClient, id))
        {
            Assert.Equal("Changed by A", current.RootElement.GetProperty("title").GetString());
            Assert.Equal(2, current.RootElement.GetProperty("version").GetInt64());
        }

        using var retriedByB = await PutTodoAsync(secondClient, id, "Changed by B", version: 2);

        Assert.Equal(HttpStatusCode.OK, retriedByB.StatusCode);
        using var retried = await ReadJsonAsync(retriedByB);
        Assert.Equal("Changed by B", retried.RootElement.GetProperty("title").GetString());
        Assert.Equal(3, retried.RootElement.GetProperty("version").GetInt64());
    }

    // AC 3, AC 4: Smazání se zastaralou verzí přes druhou instanci vrátí 409 a úkol v souboru zůstane.
    [Fact]
    public async Task TwoHostsUsingSameFile_DeleteWithStaleVersion_Returns409AndKeepsTask()
    {
        await using var firstFactory = CreateFactory("api-a");
        await using var secondFactory = CreateFactory("api-b");

        using var firstClient = firstFactory.CreateClient();
        using var secondClient = secondFactory.CreateClient();

        var id = await CreateTodoAsync(
            firstClient,
            userId: "alice",
            title: "Shared task");

        using (var completedByA = await firstClient.PostAsync($"/api/todos/{id}/complete", null))
        {
            Assert.Equal(HttpStatusCode.OK, completedByA.StatusCode);
        }

        using (var staleFromB = await secondClient.DeleteAsync($"/api/todos/{id}?version=1"))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleFromB.StatusCode);
        }

        using var current = await GetTodoAsync(firstClient, id);
        Assert.True(current.RootElement.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(2, current.RootElement.GetProperty("version").GetInt64());
    }

    // AC 1, AC 5: Soubor uložený před zavedením verzí se načte s verzí 1 a jde dál upravovat.
    [Fact]
    public async Task FileFromBeforeVersions_LoadsWithVersionOneAndAcceptsUpdate()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            StorageFilePath,
            """[{"id": "0f8fad5b-d9cb-469f-a165-70867728950e", "userId": "alice", "title": "Old task", "createdAtUtc": "2026-10-26T12:00:00Z"}]""");

        await using var factory = CreateFactory("api-old-file");
        using var client = factory.CreateClient();

        using (var loaded = await GetTodoAsync(client, id))
        {
            Assert.Equal(1, loaded.RootElement.GetProperty("version").GetInt64());
        }

        using var updated = await PutTodoAsync(client, id, "Old task updated", version: 1);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var json = await ReadJsonAsync(updated);
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt64());
        Assert.Contains("\"version\": 2", await File.ReadAllTextAsync(StorageFilePath));
    }

    private PersistentApiTestFactory CreateFactory(string instanceName) =>
        new(StorageFilePath, instanceName);

    private static Task<HttpResponseMessage> PutTodoAsync(
        HttpClient client,
        Guid id,
        string title,
        long version) =>
        client.PutAsJsonAsync(
            $"/api/todos/{id}",
            new
            {
                Title = title,
                Version = version,
            });

    private static async Task<JsonDocument> GetTodoAsync(
        HttpClient client,
        Guid id)
    {
        using var response = await client.GetAsync($"/api/todos/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

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
