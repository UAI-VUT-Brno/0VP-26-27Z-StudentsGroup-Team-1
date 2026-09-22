namespace UAI.ZeroVP.Data.InMemory;

/// <summary>
/// Nastavení paměťového úložiště.
/// V kontejneru se dá přepsat proměnnou prostředí, např. <c>Todo__SeedSampleData=false</c>.
/// </summary>
public sealed class InMemoryTodoOptions
{
    /// <summary>Název sekce v <c>appsettings.json</c>.</summary>
    public const string SectionName = "Todo";

    /// <summary>Naplnit úložiště ukázkovými úkoly při startu aplikace.</summary>
    public bool SeedSampleData { get; set; } = true;
}
