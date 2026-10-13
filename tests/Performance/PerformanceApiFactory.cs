using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UAI.ZeroVP.Tests.Performance;

/// <summary>
/// API pro zátěžové testy. Jeden běh testů zapíše přes 8 000 řádků logu, proto se logy
/// neukládají do <c>src/UAI.ZeroVP.API/logs/</c>, ale do dočasné složky systému.
/// </summary>
public sealed class PerformanceApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Složka s logy zátěžových testů, např. <c>%TEMP%\uai-zerovp-performance-tests</c>.</summary>
    public static readonly string LogDirectory = Path.Combine(Path.GetTempPath(), "uai-zerovp-performance-tests");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("PersistentLogging:FilePath", Path.Combine(LogDirectory, "uai-zerovp-.log"));
}