namespace UAI.ZeroVP.DTO;

/// <summary>
/// Počet úkolů vrácený endpointem <c>GET /api/todos/count</c>.
/// Bere ohled na stejné volitelné filtry jako seznam úkolů.</summary>
public sealed record TodoCountDto
{
    /// <summary>Počet úkolů, které odpovídají zadaným filtrům; bez filtrů celkový počet.</summary>
    public required int Count { get; init; }
}
