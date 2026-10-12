namespace UAI.ZeroVP.Tests.Performance;

/// <summary>
/// Výkonnostní testy neběží souběžně s ostatními testy – jinak by si s nimi
/// konkurovaly o procesor a naměřené časy by byly zkreslené.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceCollection
{
    public const string Name = "Performance";
}