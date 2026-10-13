namespace UAI.ZeroVP.Tests.Performance;

/// <summary>
/// Výkonnostní test, který se spouští jen na vyžádání.
/// <para>
/// Naměřené časy závisí na výkonu a aktuálním vytížení počítače, proto test není součástí
/// běžného <c>dotnet test</c>. Spustí se, když je nastavená proměnná prostředí
/// <c>RUN_PERFORMANCE_TESTS=true</c>; jinak se zobrazí jako přeskočený.
/// </para>
/// </summary>
public sealed class PerformanceFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "RUN_PERFORMANCE_TESTS";

    public PerformanceFactAttribute()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase);

        if (!enabled)
        {
            Skip = $"Výkonnostní test se spouští jen s proměnnou prostředí {EnvironmentVariable}=true.";
        }
    }
}