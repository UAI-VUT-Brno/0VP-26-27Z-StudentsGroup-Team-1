using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UAI.ZeroVP.Tests.OpenApi;

/// <summary>
/// API pro testy dokumentu OpenAPI. Běží v prostředí Development, kde je Swagger zapnutý.
/// Logy se neukládají do <c>src/UAI.ZeroVP.API/logs/</c>, ale do dočasné složky systému.
/// </summary>
public sealed class OpenApiTestFactory : WebApplicationFactory<Program>
{
    /// <summary>Složka s logy testů, např. <c>%TEMP%\uai-zerovp-openapi-tests</c>.</summary>
    public static readonly string LogDirectory = Path.Combine(Path.GetTempPath(), "uai-zerovp-openapi-tests");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("PersistentLogging:FilePath", Path.Combine(LogDirectory, "uai-zerovp-.log"));
}
