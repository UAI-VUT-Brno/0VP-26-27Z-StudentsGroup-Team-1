using Microsoft.Extensions.Hosting;
using UAI.ZeroVP.Data.Abstractions;

namespace UAI.ZeroVP.Data.JsonFile;

/// <summary>
/// Napojí soubor s úkoly na životní cyklus aplikace: při startu se úkoly načtou,
/// při zastavení (Ctrl+C, <c>docker stop</c>) se uloží.
/// </summary>
public sealed class JsonFileTodoPersistenceService : IHostedService
{
    private readonly ITodoRepository _repository;
    private readonly JsonFileTodoStore _store;

    /// <summary>
    /// Host vytváří hostované služby hned při startu. Tím, že služba repozitář přebírá
    /// v konstruktoru, se repozitář vytvoří – a soubor načte – už při startu,
    /// ne až s prvním HTTP požadavkem.
    /// </summary>
    public JsonFileTodoPersistenceService(ITodoRepository repository, JsonFileTodoStore store)
    {
        _repository = repository;
        _store = store;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Token se nepředává dál: zrušení by znamenalo neuložená data a o vypnutí jde právě teď.
        var items = await _repository.GetAllAsync(cancellationToken: CancellationToken.None);
        _store.Save(items);
    }
}
