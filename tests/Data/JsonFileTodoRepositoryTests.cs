using Microsoft.Extensions.Logging.Testing;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.JsonFile;

namespace UAI.ZeroVP.Tests.Data;

/// <summary>
/// Úložiště nad sdíleným souborem JSON. Dvě instance API se tu simulují dvěma repozitáři
/// nad stejným souborem – každý má vlastní zámek v procesu, takže se hlídají jen zámkem souboru,
/// stejně jako dva samostatné procesy.
/// </summary>
public sealed class JsonFileTodoRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "uai-zerovp-json-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "todos.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonFileTodoRepository CreateRepository(bool seedSampleData = false, TimeSpan? lockTimeout = null)
    {
        var storageOptions = new JsonFileTodoOptions { FilePath = FilePath };
        if (lockTimeout is not null)
        {
            storageOptions.LockTimeout = lockTimeout.Value;
        }

        return new JsonFileTodoRepository(
            new JsonFileTodoStore(storageOptions, new FakeLogger<JsonFileTodoStore>()),
            storageOptions,
            new InMemoryTodoOptions { SeedSampleData = seedSampleData });
    }

    private static TodoItem CreateItem(string title = "Úkol") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    // --- Sdílení mezi instancemi ----------------------------------------------

    [Fact]
    public async Task Add_ThroughOneInstance_IsVisibleThroughAnother()
    {
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();
        var item = CreateItem("Z instance A");

        await instanceA.AddAsync(item);

        Assert.Equal(item.Id, Assert.Single(await instanceB.GetAllAsync()).Id);
        Assert.NotNull(await instanceB.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task UpdateAndDelete_ThroughOneInstance_AreVisibleThroughAnother()
    {
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();
        var kept = CreateItem("Zůstane");
        var deleted = CreateItem("Smaže se");
        await instanceA.AddRangeAsync([kept, deleted]);

        kept.IsCompleted = true;
        Assert.True(await instanceB.UpdateAsync(kept));
        Assert.True(await instanceB.DeleteAsync(deleted.Id));

        var remaining = Assert.Single(await instanceA.GetAllAsync());
        Assert.Equal(kept.Id, remaining.Id);
        Assert.True(remaining.IsCompleted);
    }

    [Fact]
    public async Task ConcurrentWrites_FromTwoInstances_LoseNoTask()
    {
        const int tasksPerInstance = 25;
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();

        // Obě instance zapisují současně. Bez zámku by si navzájem přepisovaly soubor
        // a část úkolů by zmizela.
        await Task.WhenAll(
            Enumerable.Range(0, tasksPerInstance).SelectMany(_ => new[]
            {
                Task.Run(() => instanceA.AddAsync(CreateItem("A"))),
                Task.Run(() => instanceB.AddAsync(CreateItem("B")))
            }));

        Assert.Equal(2 * tasksPerInstance, await CreateRepository().CountAsync());
    }

    // --- Kontrola verze mezi instancemi -------------------------------------

    // AC 2, AC 3, AC 4: Zápis přes jednu instanci zneplatní verzi, kterou drží druhá.
    [Fact]
    public async Task UpdateIfVersionMatches_ThroughOneInstance_MakesVersionStaleOnAnother()
    {
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();
        var item = CreateItem("Původní");
        await instanceA.AddAsync(item);
        var readByB = await instanceB.GetByIdAsync(item.Id);
        Assert.NotNull(readByB);
        Assert.Equal(1, readByB.Version);

        var fromA = item.Clone();
        fromA.Title = "Z instance A";
        var resultA = await instanceA.UpdateIfVersionMatchesAsync(fromA, expectedVersion: 1);

        readByB.Title = "Z instance B";
        var resultB = await instanceB.UpdateIfVersionMatchesAsync(readByB, expectedVersion: readByB.Version);

        Assert.Equal(TodoWriteStatus.Success, resultA.Status);
        Assert.Equal(TodoWriteStatus.Conflict, resultB.Status);
        Assert.NotNull(resultB.Item);
        Assert.Equal(2, resultB.Item.Version);
        var stored = await CreateRepository().GetByIdAsync(item.Id);
        Assert.NotNull(stored);
        Assert.Equal("Z instance A", stored.Title);
        Assert.Equal(2, stored.Version);
    }

    // AC 3: Smazání se zastaralou verzí úkol v souboru ponechá.
    [Fact]
    public async Task DeleteIfVersionMatches_StaleVersion_KeepsTaskInFile()
    {
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();
        var item = CreateItem("Původní");
        await instanceA.AddAsync(item);
        item.Title = "Novější";
        await instanceA.UpdateIfVersionMatchesAsync(item, expectedVersion: 1);

        var result = await instanceB.DeleteIfVersionMatchesAsync(item.Id, expectedVersion: 1);

        Assert.Equal(TodoWriteStatus.Conflict, result.Status);
        var stored = await CreateRepository().GetByIdAsync(item.Id);
        Assert.NotNull(stored);
        Assert.Equal("Novější", stored.Title);
        Assert.Equal(2, stored.Version);
    }

    // AC 2: Smazání se shodnou verzí úkol ze souboru odstraní.
    [Fact]
    public async Task DeleteIfVersionMatches_MatchingVersion_RemovesTaskFromFile()
    {
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();
        var item = CreateItem();
        await instanceA.AddAsync(item);

        var result = await instanceB.DeleteIfVersionMatchesAsync(item.Id, expectedVersion: 1);

        Assert.Equal(TodoWriteStatus.Success, result.Status);
        Assert.Empty(await CreateRepository().GetAllAsync());
    }

    // AC 6: Platná verze u neexistujícího úkolu vrátí NotFound a nic nepřidá.
    [Fact]
    public async Task UpdateIfVersionMatches_UnknownItem_ReturnsNotFound()
    {
        var repository = CreateRepository();

        var result = await repository.UpdateIfVersionMatchesAsync(CreateItem(), expectedVersion: 1);

        Assert.Equal(TodoWriteStatus.NotFound, result.Status);
        Assert.Equal(0, await CreateRepository().CountAsync());
    }

    // AC 7: Ze souběžných zápisů se stejnou verzí ze dvou instancí projde právě jeden.
    [Fact]
    public async Task ConcurrentUpdatesWithSameVersion_FromTwoInstances_ExactlyOneSucceeds()
    {
        const int updatesPerInstance = 10;
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();
        var item = CreateItem();
        await instanceA.AddAsync(item);

        // Obě instance zapisují současně se stejnou verzí 1. Kontrola a zápis běží pod jedním
        // zámkem souboru, takže první zápis verzi zvýší a všechny ostatní uvidí konflikt.
        var results = await Task.WhenAll(
            Enumerable.Range(0, updatesPerInstance).SelectMany(i => new[]
            {
                Task.Run(() => instanceA.UpdateIfVersionMatchesAsync(WithTitle(item, $"A {i}"), expectedVersion: 1)),
                Task.Run(() => instanceB.UpdateIfVersionMatchesAsync(WithTitle(item, $"B {i}"), expectedVersion: 1))
            }));

        Assert.Single(results, result => result.Status == TodoWriteStatus.Success);
        Assert.Equal(2 * updatesPerInstance - 1, results.Count(result => result.Status == TodoWriteStatus.Conflict));
        var stored = await CreateRepository().GetByIdAsync(item.Id);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Version);
    }

    private static TodoItem WithTitle(TodoItem item, string title)
    {
        var copy = item.Clone();
        copy.Title = title;
        return copy;
    }

    // --- Chybějící soubor ---------------------------------------------------

    [Fact]
    public async Task MissingFile_SampleDataAreCreatedOnceAndShared()
    {
        var instanceA = CreateRepository(seedSampleData: true);
        var instanceB = CreateRepository(seedSampleData: true);

        var first = await instanceA.GetAllAsync();
        var second = await instanceB.GetAllAsync();

        // Ukázková data se do souboru uloží hned při prvním čtení – jinak by každé čtení
        // vytvořilo nové úkoly s jinými identifikátory.
        Assert.Equal(4, first.Count);
        Assert.Equal(first.Select(item => item.Id), second.Select(item => item.Id));
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public async Task FailedWrite_LeavesTheFileUnchanged()
    {
        var repository = CreateRepository();
        var item = CreateItem();
        await repository.AddAsync(item);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddRangeAsync([CreateItem(), item]));

        Assert.Equal(item.Id, Assert.Single(await CreateRepository().GetAllAsync()).Id);
    }

    // --- Zámek ----------------------------------------------------------------

    [Fact]
    public async Task LockedFile_WaitsUntilTheOtherInstanceReleasesIt()
    {
        var repository = CreateRepository();
        Directory.CreateDirectory(_directory);
        var otherInstance = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var adding = repository.AddAsync(CreateItem());
        await Task.Delay(100);
        Assert.False(adding.IsCompleted);

        await otherInstance.DisposeAsync();
        await adding;

        Assert.Equal(1, await repository.CountAsync());
    }

    [Fact]
    public async Task LockedFile_ThrowsTimeoutWhenItIsNotReleased()
    {
        var repository = CreateRepository(lockTimeout: TimeSpan.FromMilliseconds(100));
        Directory.CreateDirectory(_directory);
        await using var otherInstance = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<TimeoutException>(() => repository.CountAsync());
    }

    [Fact]
    public async Task CancelledToken_ThrowsWithoutTouchingTheFile()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateRepository().GetAllAsync(cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(_directory));
    }
}
