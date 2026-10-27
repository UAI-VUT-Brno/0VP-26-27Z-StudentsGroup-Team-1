using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.JsonFile;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.Data;

/// <summary>
/// Soubor JSON s úkoly: uložení a zpětné načtení, chybějící soubor, poškozený soubor.
/// Každý test pracuje ve vlastní dočasné složce, kterou na konci smaže.
/// </summary>
public sealed class JsonFileTodoStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "uai-zerovp-json-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLogger<JsonFileTodoStore> _logger = new();

    private string FilePath => Path.Combine(_directory, "todos.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonFileTodoStore CreateStore(string? filePath = null) =>
        new(new JsonFileTodoOptions { FilePath = filePath ?? FilePath }, _logger, new FakeTimeProvider(Now));

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content);
    }

    private static TodoItem CreateItem(string title = "Úkol") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    // --- Konstruktor --------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_MissingFilePath_Throws(string? filePath)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new JsonFileTodoStore(new JsonFileTodoOptions { FilePath = filePath }, _logger));
    }

    // --- Uložení a načtení --------------------------------------------------

    [Fact]
    public void SaveThenLoad_ReturnsAllFields()
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid(),
            UserId = "jan.novak",
            Title = "Napsat Dockerfile",
            Description = "Vícefázový build",
            Priority = TodoPriority.High,
            IsCompleted = true,
            DueAtUtc = Now.AddDays(7),
            CreatedAtUtc = Now.AddDays(-1),
            CompletedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        CreateStore().Save([item]);
        var loaded = Assert.Single(CreateStore().Load()!);

        Assert.Equivalent(item, loaded, strict: true);
    }

    [Fact]
    public void Load_FileFromBeforeUsers_LoadsTasksWithoutUser()
    {
        // Soubor uložený před zavedením userId toto pole nemá – úkoly se musí načíst dál.
        WriteFile("""[{"id": "0f8fad5b-d9cb-469f-a165-70867728950e", "title": "Starý úkol", "createdAtUtc": "2026-10-26T12:00:00Z"}]""");

        var loaded = Assert.Single(CreateStore().Load()!);

        Assert.Equal("Starý úkol", loaded.Title);
        Assert.Null(loaded.UserId);
    }

    [Fact]
    public void SaveThenLoad_KeepsAllItems()
    {
        var items = new[] { CreateItem("A"), CreateItem("B"), CreateItem("C") };

        CreateStore().Save(items);
        var loaded = CreateStore().Load()!;

        Assert.Equal(items.Select(item => item.Id).Order(), loaded.Select(item => item.Id).Order());
    }

    [Fact]
    public void SaveThenLoad_EmptyList_ReturnsEmptyList()
    {
        CreateStore().Save([]);

        Assert.Empty(CreateStore().Load()!);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nestedPath = Path.Combine(_directory, "a", "b", "todos.json");

        CreateStore(nestedPath).Save([CreateItem()]);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void Save_OverwritesPreviousContentAndLeavesNoTempFile()
    {
        var store = CreateStore();
        store.Save([CreateItem("Starý")]);

        var newItem = CreateItem("Nový");
        store.Save([newItem]);

        Assert.Equal(newItem.Id, Assert.Single(store.Load()!).Id);
        Assert.Equal(["todos.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_WritesReadableJson()
    {
        var item = CreateItem();
        item.Priority = TodoPriority.High;

        CreateStore().Save([item]);
        var json = File.ReadAllText(FilePath);

        // Stejné konvence jako API – soubor se dá prohlédnout i opravit ručně.
        Assert.Contains("\"title\": \"Úkol\"", json);
        Assert.Contains("\"priority\": \"High\"", json);
    }

    [Fact]
    public void Save_LogsCount()
    {
        CreateStore().Save([CreateItem(), CreateItem()]);

        Assert.Contains("Saved 2 tasks", _logger.LatestRecord.Message);
    }

    // --- Chybějící soubor ---------------------------------------------------

    [Fact]
    public void Load_MissingFile_ReturnsNullAndCreatesNothing()
    {
        Assert.Null(CreateStore().Load());
        Assert.False(Directory.Exists(_directory));
        Assert.Equal(LogLevel.Information, _logger.LatestRecord.Level);
    }

    // --- Poškozený soubor ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("{ tohle není JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{\"title\": \"Bez Id a data vytvoření\"}]")]
    [InlineData("[{\"id\": \"0f8fad5b-d9cb-469f-a165-70867728950e\", \"title\": null, \"createdAtUtc\": \"2026-10-26T12:00:00Z\"}]")]
    [InlineData("[{\"id\": \"0f8fad5b-d9cb-469f-a165-70867728950e\", \"title\": \"A\", \"priority\": \"Urgent\", \"createdAtUtc\": \"2026-10-26T12:00:00Z\"}]")]
    public void Load_CorruptedFile_ReturnsNullAndLogsError(string content)
    {
        WriteFile(content);

        Assert.Null(CreateStore().Load());

        var record = _logger.LatestRecord;
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Contains(FilePath, record.Message);
    }

    [Fact]
    public void Load_DuplicateIds_IsTreatedAsCorrupted()
    {
        var item = CreateItem();
        CreateStore().Save([item]);
        var single = File.ReadAllText(FilePath).Trim().TrimStart('[').TrimEnd(']');
        WriteFile($"[{single},{single}]");

        Assert.Null(CreateStore().Load());
        Assert.Equal(LogLevel.Error, _logger.LatestRecord.Level);
    }

    [Fact]
    public void Load_CorruptedFile_IsMovedAsideSoTheNextSaveKeepsIt()
    {
        WriteFile("{ poškozeno");
        var store = CreateStore();

        store.Load();
        store.Save([CreateItem()]);

        var backupPath = FilePath + ".corrupt-20261026T120000Z";
        Assert.Equal("{ poškozeno", File.ReadAllText(backupPath));
        Assert.Single(store.Load()!);
        Assert.Contains(backupPath, _logger.Collector.GetSnapshot().Single(r => r.Level == LogLevel.Error).Message);
    }
}
