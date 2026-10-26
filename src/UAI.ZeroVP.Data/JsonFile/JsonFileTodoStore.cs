using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using UAI.ZeroVP.Data.Entities;

namespace UAI.ZeroVP.Data.JsonFile;

/// <summary>
/// Čte a zapisuje úkoly do jednoho souboru JSON, aby přežily restart aplikace či kontejneru.
/// <para>
/// Samotná data za běhu dál drží <see cref="InMemory.InMemoryTodoRepository"/>;
/// soubor se čte jednou při startu a zapisuje při zastavení aplikace.
/// Chyby čtení a zápisu (práva, plný disk) se podle zadání zatím neošetřují.
/// </para>
/// </summary>
public sealed class JsonFileTodoStore
{
    // Stejné konvence jako API: camelCase a výčty jako řetězce – soubor jde číst i ručně.
    // RespectNullableAnnotations odmítne např. "title": null, i když vlastnost v souboru je.
    // Encoder zapisuje diakritiku přímo ("Úkol"), ne jako "\u00DAkol".
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly ILogger<JsonFileTodoStore> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Vytvoří úložiště nad souborem z nastavení.</summary>
    /// <param name="options">Nastavení; <see cref="JsonFileTodoOptions.FilePath"/> musí být vyplněná.</param>
    /// <param name="logger">Log pro informace o načtení, uložení a poškozeném souboru.</param>
    /// <param name="timeProvider">Zdroj času pro název zálohy poškozeného souboru.</param>
    public JsonFileTodoStore(JsonFileTodoOptions options, ILogger<JsonFileTodoStore> logger, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FilePath);
        ArgumentNullException.ThrowIfNull(logger);

        _filePath = options.FilePath;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Cesta k souboru s úkoly.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Načte úkoly ze souboru.
    /// </summary>
    /// <returns>
    /// Načtené úkoly, nebo <c>null</c>, pokud soubor neexistuje nebo je poškozený –
    /// pak se začíná od nuly. Poškozený soubor se přejmenuje, aby ho další uložení nepřepsalo.
    /// </returns>
    public IReadOnlyList<TodoItem>? Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("Task file {FilePath} does not exist, starting from scratch.", _filePath);
            return null;
        }

        try
        {
            var items = Deserialize(File.ReadAllText(_filePath));
            _logger.LogInformation("Loaded {Count} tasks from {FilePath}.", items.Count, _filePath);
            return items;
        }
        catch (JsonException exception)
        {
            var backupPath = BackupCorruptedFile();
            _logger.LogError(
                exception,
                "Task file {FilePath} is corrupted and was moved to {BackupPath}, starting from scratch. Reason: {Reason}",
                _filePath,
                backupPath,
                exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Uloží úkoly do souboru. Zapisuje se nejdřív do dočasného souboru, který pak nahradí původní –
    /// přerušený zápis tak nezanechá napůl zapsaný soubor.
    /// </summary>
    /// <param name="items">Všechny úkoly, které se mají uložit.</param>
    public void Save(IEnumerable<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items.ToList();

        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(list, SerializerOptions));
        File.Move(tempPath, _filePath, overwrite: true);

        _logger.LogInformation("Saved {Count} tasks to {FilePath}.", list.Count, _filePath);
    }

    /// <summary>
    /// Převede obsah souboru na úkoly. Vše, co neodpovídá očekávanému tvaru,
    /// hlásí jako <see cref="JsonException"/>, aby se s tím zacházelo jako s poškozeným souborem.
    /// </summary>
    private static List<TodoItem> Deserialize(string json)
    {
        var items = JsonSerializer.Deserialize<List<TodoItem?>>(json, SerializerOptions)
            ?? throw new JsonException("The file contains null instead of a list of tasks.");

        if (items.Contains(null))
        {
            throw new JsonException("The list of tasks contains null.");
        }

        var duplicateId = items.GroupBy(item => item!.Id).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new JsonException($"The list of tasks contains the ID {duplicateId} more than once.");
        }

        return items!;
    }

    /// <summary>Přejmenuje poškozený soubor na <c>todos.json.corrupt-20261026T120000Z</c> a vrátí novou cestu.</summary>
    private string BackupCorruptedFile()
    {
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var backupPath = $"{_filePath}.corrupt-{timestamp}";
        File.Move(_filePath, backupPath, overwrite: true);
        return backupPath;
    }
}
