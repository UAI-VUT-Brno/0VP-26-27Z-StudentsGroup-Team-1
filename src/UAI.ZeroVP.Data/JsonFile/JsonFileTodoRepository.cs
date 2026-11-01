using System.Diagnostics;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Data.JsonFile;

/// <summary>
/// Úložiště úkolů nad jedním souborem JSON, který může sdílet několik běžících instancí API.
/// <para>
/// Každá operace si soubor zamkne, přečte aktuální obsah, provede změnu a – jde-li o zápis –
/// soubor hned uloží. Instance tak vidí změny ostatních a souběžné zápisy se nepřepisují.
/// </para>
/// <para>
/// Samotnou logiku operací (řazení, filtr, "všechno, nebo nic" u dávky) nepíšeme znovu:
/// nad načtenými úkoly se pro každou operaci vytvoří <see cref="InMemoryTodoRepository"/>.
/// </para>
/// </summary>
public sealed class JsonFileTodoRepository : ITodoRepository
{
    // Jak často se znovu zkouší zamknout soubor, který drží jiná instance.
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(10);

    private readonly JsonFileTodoStore _store;
    private readonly InMemoryTodoOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lockTimeout;
    private readonly string _lockFilePath;

    // Požadavky uvnitř jednoho procesu se řadí tady a na zámek souboru nečekají ve smyčce.
    private readonly SemaphoreSlim _processLock = new(1, 1);

    /// <summary>Vytvoří úložiště nad souborem, se kterým pracuje <paramref name="store"/>.</summary>
    /// <param name="store">Čtení a zápis souboru s úkoly.</param>
    /// <param name="storageOptions">Nastavení souboru – z něj se bere čekání na zámek.</param>
    /// <param name="options">Nastavení ukázkových dat pro případ, že soubor ještě neexistuje.</param>
    /// <param name="timeProvider">Zdroj času – umožňuje čas podvrhnout v testech.</param>
    public JsonFileTodoRepository(
        JsonFileTodoStore store,
        JsonFileTodoOptions storageOptions,
        InMemoryTodoOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(storageOptions);

        _store = store;
        _options = options ?? new InMemoryTodoOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lockTimeout = storageOptions.LockTimeout;
        _lockFilePath = store.FilePath + ".lock";
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TodoItem>> GetAllAsync(string? userId = null, bool? isCompleted = null, CancellationToken cancellationToken = default) =>
    ExecuteAsync(repository => repository.GetAllAsync(userId, isCompleted, cancellationToken), isWrite: false, cancellationToken);

    /// <inheritdoc />
    public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(repository => repository.GetByIdAsync(id, cancellationToken), isWrite: false, cancellationToken);

    /// <inheritdoc />
    public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return ExecuteAsync(repository => repository.AddAsync(item, cancellationToken), isWrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        return ExecuteAsync(repository => repository.AddRangeAsync(items, cancellationToken), isWrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return ExecuteAsync(repository => repository.UpdateAsync(item, cancellationToken), isWrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(repository => repository.DeleteAsync(id, cancellationToken), isWrite: true, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(repository => repository.CountAsync(cancellationToken), isWrite: false, cancellationToken);

    /// <summary>
    /// Zamkne soubor, načte z něj úkoly, provede nad nimi operaci a po zápisu soubor uloží.
    /// Chybějící nebo poškozený soubor se uloží i po čtení – jinak by každé čtení
    /// vložilo nová ukázková data s jinými identifikátory.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<InMemoryTodoRepository, Task<T>> operation, bool isWrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _processLock.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);

            var loaded = _store.Load();
            var repository = new InMemoryTodoRepository(_options, _timeProvider, loaded);

            var result = await operation(repository);

            if (isWrite || loaded is null)
            {
                _store.Save(await repository.GetAllAsync(cancellationToken: CancellationToken.None));
            }

            return result;
        }
        finally
        {
            _processLock.Release();
        }
    }

    /// <summary>
    /// Otevře zámkový soubor <c>todos.json.lock</c> výhradně pro sebe. Dokud je otevřený,
    /// ostatní procesy ho otevřít nemohou a čekají. Soubor se po uvolnění nemaže –
    /// smazání by mohlo pustit dva procesy dovnitř současně.
    /// </summary>
    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_lockFilePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Čekání se měří skutečnými hodinami, ne přes TimeProvider – podvržený čas
        // v testech stojí a čekání by nikdy neskončilo.
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waiting.Elapsed < _lockTimeout)
            {
                // Soubor drží jiná instance – chvíli počkáme a zkusíme to znovu.
                await Task.Delay(LockRetryDelay, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new TimeoutException(
                    $"Soubor {_lockFilePath} se nepodařilo zamknout do {_lockTimeout.TotalSeconds} s.", exception);
            }
        }
    }
}