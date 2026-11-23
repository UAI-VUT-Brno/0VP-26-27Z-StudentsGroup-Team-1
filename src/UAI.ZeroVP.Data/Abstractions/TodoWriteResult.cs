using UAI.ZeroVP.Data.Entities;

namespace UAI.ZeroVP.Data.Abstractions;

/// <summary>
/// Výsledek zápisu s kontrolou verze, viz <see cref="ITodoRepository.UpdateIfVersionMatchesAsync"/>
/// a <see cref="ITodoRepository.DeleteIfVersionMatchesAsync"/>.
/// Konflikt se hlásí výsledkem, ne výjimkou: při souběžné práci jde o běžný stav, ne o chybu.
/// </summary>
public sealed record TodoWriteResult
{
    /// <summary>Jak zápis dopadl.</summary>
    public required TodoWriteStatus Status { get; init; }

    /// <summary>
    /// Úkol k výsledku. Po úspěšné úpravě uložený stav, po úspěšném smazání smazaný úkol,
    /// při konfliktu aktuálně uložený stav – API z něj bere verzi, kterou uvede v odpovědi 409.
    /// Při <see cref="TodoWriteStatus.NotFound"/> je <c>null</c>.
    /// </summary>
    public TodoItem? Item { get; init; }

    /// <summary>Zápis proběhl.</summary>
    /// <param name="item">Uložený úkol po úpravě, nebo smazaný úkol.</param>
    public static TodoWriteResult Success(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new TodoWriteResult { Status = TodoWriteStatus.Success, Item = item };
    }

    /// <summary>Uložená verze se liší od očekávané; nic se nezapsalo.</summary>
    /// <param name="current">Aktuálně uložený úkol.</param>
    public static TodoWriteResult Conflict(TodoItem current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return new TodoWriteResult { Status = TodoWriteStatus.Conflict, Item = current };
    }

    /// <summary>Úkol neexistuje; nic se nezapsalo.</summary>
    public static TodoWriteResult NotFound() => new() { Status = TodoWriteStatus.NotFound };
}
