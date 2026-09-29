namespace UAI.ZeroVP.Tests;

/// <summary>
/// Ověřuje, že testovací projekt je správně zapojený do řešení
/// a <c>dotnet test</c> ho najde a spustí.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void TestProject_Runs()
    {
        Assert.True(true);
    }
}
