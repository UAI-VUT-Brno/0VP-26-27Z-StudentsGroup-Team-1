using Microsoft.Extensions.Logging.Testing;
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
