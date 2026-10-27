using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.JsonFile;

namespace UAI.ZeroVP.Tests.Data;

/// <summary>
/// Registrace úložiště se souborem JSON a jeho chování během života aplikace.
/// Restart se simuluje tak, že se zastaví jeden host a nad stejným souborem se spustí nový.
/// </summary>
public sealed class JsonFileTodoDataTests : IDisposable
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

    private IHost CreateHost(bool seedSampleData = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddJsonFileTodoData(
            storage => storage.FilePath = FilePath,
            options => options.SeedSampleData = seedSampleData);
        return builder.Build();
    }

    private static TodoItem CreateItem(string title = "Úkol") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    // --- Registrace ---------------------------------------------------------
    [Fact]
    public void AddJsonFileTodoData_RegistersSharedFileRepositoryAsSingleton()
    {
        using var provider = new ServiceCollection()
            .AddJsonFileTodoData(storage => storage.FilePath = FilePath)
            .BuildServiceProvider();

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var repository = firstScope.ServiceProvider.GetRequiredService<ITodoRepository>();

        Assert.IsType<JsonFileTodoRepository>(repository);
        Assert.Same(repository, secondScope.ServiceProvider.GetRequiredService<ITodoRepository>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddJsonFileTodoData_MissingFilePath_Throws(string? filePath)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ServiceCollection().AddJsonFileTodoData(storage => storage.FilePath = filePath));
    }

    [Fact]
    public async Task AddJsonFileTodoData_ExistingFile_LoadsItWithoutSampleData()
    {
        var item = CreateItem("Ze souboru");
        new JsonFileTodoStore(new JsonFileTodoOptions { FilePath = FilePath }, new FakeLogger<JsonFileTodoStore>())
            .Save([item]);

        using var provider = new ServiceCollection()
            .AddJsonFileTodoData(storage => storage.FilePath = FilePath, options => options.SeedSampleData = true)
            .BuildServiceProvider();
        var repository = provider.GetRequiredService<ITodoRepository>();

        Assert.Equal(item.Id, Assert.Single(await repository.GetAllAsync()).Id);
    }

    [Fact]
    public async Task AddJsonFileTodoData_MissingFile_StartsWithSampleDataWhenEnabled()
    {
        using var provider = new ServiceCollection()
            .AddJsonFileTodoData(storage => storage.FilePath = FilePath, options => options.SeedSampleData = true)
            .BuildServiceProvider();

        Assert.Equal(4, await provider.GetRequiredService<ITodoRepository>().CountAsync());
    }

    // --- Životní cyklus aplikace ----------------------------------------------

    [Fact]
    public async Task Add_IsInTheFileImmediately_WithoutStoppingTheApplication()
    {
        var item = CreateItem("Hned v souboru");

        using var host = CreateHost();
        await host.StartAsync();
        await host.Services.GetRequiredService<ITodoRepository>().AddAsync(item);

        var saved = new JsonFileTodoStore(new JsonFileTodoOptions { FilePath = FilePath }, new FakeLogger<JsonFileTodoStore>())
            .Load();
        await host.StopAsync();

        Assert.Equal(item.Id, Assert.Single(saved!).Id);
    }

    [Fact]
    public async Task Stop_SavesTasks_AndNextStartLoadsThem()
    {
        var item = CreateItem("Přežije restart");

        using (var host = CreateHost())
        {
            await host.StartAsync();
            await host.Services.GetRequiredService<ITodoRepository>().AddAsync(item);
            await host.StopAsync();
        }

        Assert.True(File.Exists(FilePath));

        using (var host = CreateHost(seedSampleData: true))
        {
            await host.StartAsync();
            var loaded = await host.Services.GetRequiredService<ITodoRepository>().GetAllAsync();
            await host.StopAsync();

            Assert.Equal(item.Id, Assert.Single(loaded).Id);
        }
    }

    [Fact]
    public async Task Stop_SavesChangesAndDeletions()
    {
        var kept = CreateItem("Zůstane");
        var deleted = CreateItem("Smaže se");

        using (var host = CreateHost())
        {
            await host.StartAsync();
            var repository = host.Services.GetRequiredService<ITodoRepository>();
            await repository.AddRangeAsync([kept, deleted]);
            kept.IsCompleted = true;
            await repository.UpdateAsync(kept);
            await repository.DeleteAsync(deleted.Id);
            await host.StopAsync();
        }

        using (var host = CreateHost())
        {
            await host.StartAsync();
            var loaded = Assert.Single(await host.Services.GetRequiredService<ITodoRepository>().GetAllAsync());
            await host.StopAsync();

            Assert.Equal(kept.Id, loaded.Id);
            Assert.True(loaded.IsCompleted);
        }
    }

    [Fact]
    public async Task Start_CorruptedFile_StartsEmptyAndKeepsTheBrokenFileAside()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{ poškozeno");

        using var host = CreateHost();
        await host.StartAsync();
        var count = await host.Services.GetRequiredService<ITodoRepository>().CountAsync();
        await host.StopAsync();

        Assert.Equal(0, count);
        Assert.Single(Directory.GetFiles(_directory, "todos.json.corrupt-*"));
    }
}
