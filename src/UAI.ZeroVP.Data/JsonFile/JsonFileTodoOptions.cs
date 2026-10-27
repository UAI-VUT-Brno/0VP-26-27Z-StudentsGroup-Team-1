namespace UAI.ZeroVP.Data.JsonFile;

/// <summary>
/// Nastavení ukládání úkolů do souboru JSON.
/// V kontejneru se dá přepsat proměnnou prostředí, např. <c>Todo__Storage__FilePath=/data/todos.json</c>.
/// </summary>
public sealed class JsonFileTodoOptions
{
    /// <summary>Název sekce v <c>appsettings.json</c>.</summary>
    public const string SectionName = "Todo:Storage";

    /// <summary>
    /// Absolutní cesta k souboru s úkoly. Prázdná hodnota = bez souboru, data jen v paměti.
    /// </summary>
    public string? FilePath { get; set; }
}
