using UAI.ZeroVP.Data.Entities;

namespace UAI.ZeroVP.Data.Abstractions;

/// <summary>
/// Kontrakt úložiště úkolů.
/// Metody jsou asynchronní záměrně: dnes za rozhraním stojí paměť,
/// zítra může stát databáze v jiném kontejneru a API se nemusí měnit.
/// </summary>
public interface ITodoRepository
{
    /// <summary>Vrátí všechny úkoly, volitelně filtrované podle stavu dokončení.</summary>
    /// <param name="isCompleted">Filtr na dokončenost; <c>null</c> = bez filtru.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <param name="userId">Filtr podle uživatele; <c>null</c> = bez filtru.</param>
    Task<IReadOnlyList<TodoItem>> GetAllAsync(string? userId = null, bool? isCompleted = null, CancellationToken cancellationToken = default);

    /// <summary>Vrátí úkol podle identifikátoru, nebo <c>null</c> pokud neexistuje.</summary>
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Uloží nový úkol a vrátí jeho výslednou podobu včetně přiděleného <c>Id</c>.</summary>
    Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uloží několik nových úkolů najednou a vrátí je ve stejném pořadí.
    /// Uloží se buď všechny, nebo – při chybě – žádný.
    /// </summary>
    Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default);

    /// <summary>Přepíše existující úkol. Vrací <c>false</c>, pokud záznam neexistuje.</summary>
    Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>Smaže úkol. Vrací <c>false</c>, pokud záznam neexistuje.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Počet úkolů v úložišti – využívá ho diagnostický endpoint.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
