using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// API pro testy přes HTTP. Úložiště začíná prázdné (bez ukázkových úkolů), úkoly se neukládají
/// do souboru a logy se zapisují do dočasné složky systému, ne do <c>src/UAI.ZeroVP.API/logs/</c>.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    /// <summary>Složka s logy testů, např. <c>%TEMP%\uai-zerovp-api-tests</c>.</summary>
    public static readonly string LogDirectory = Path.Combine(Path.GetTempPath(), "uai-zerovp-api-tests");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("PersistentLogging:FilePath", Path.Combine(LogDirectory, "uai-zerovp-.log"));
        builder.UseSetting("Todo:Storage:FilePath", "");
        builder.UseSetting("Todo:SeedSampleData", "false");
    }
}