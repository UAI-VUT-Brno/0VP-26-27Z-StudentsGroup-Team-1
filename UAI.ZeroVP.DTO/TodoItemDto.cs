namespace UAI.ZeroVP.DTO;

/// <summary>
/// Úkol tak, jak ho API vrací klientovi.
/// Záměrně neobsahuje interní sloupce entity (např. <c>UpdatedAtUtc</c>) –
/// to je hlavní důvod, proč DTO existuje jako samostatná vrstva.
/// </summary>
public sealed record TodoItemDto
{
    /// <summary>Jednoznačný identifikátor úkolu.</summary>
    public required Guid Id { get; init; }

    /// <summary>Krátký název úkolu.</summary>
    public required string Title { get; init; }

    /// <summary>Volitelný podrobnější popis.</summary>
    public string? Description { get; init; }

    /// <summary>Priorita úkolu.</summary>
    public TodoPriority Priority { get; init; }

    /// <summary>Příznak, zda je úkol hotový.</summary>
    public bool IsCompleted { get; init; }

    /// <summary>Okamžik vytvoření (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Okamžik dokončení (UTC), nebo <c>null</c> pokud úkol hotový není.</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }
}
