using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using UAI.ZeroVP.Data.JsonFile;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// API factory pro integrační testy se skutečným JSON úložištěm.
/// Každá instance dostane explicitní cestu k dočasnému souboru,
/// takže více hostů může sdílet stejné úložiště.
/// </summary>
public sealed class PersistentApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _storageFilePath;
    private readonly string? _instanceName;

    public PersistentApiTestFactory(
        string storageFilePath,
        string? instanceName = null)
    {
        _storageFilePath = storageFilePath;
        _instanceName = instanceName;
    }

    /// <summary>
    /// Zachycené logy JSON úložiště pro ověření chybových stavů v integračních testech.
    /// </summary>
    public FakeLogger<JsonFileTodoStore> StoreLogger { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "PersistentLogging:FilePath",
            Path.Combine(ApiTestFactory.LogDirectory, "uai-zerovp-.log"));

        builder.UseSetting("Todo:Storage:FilePath", _storageFilePath);
        builder.UseSetting("Todo:SeedSampleData", "false");

        if (!string.IsNullOrWhiteSpace(_instanceName))
        {
            builder.UseSetting("Instance:Name", _instanceName);
        }

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogger<JsonFileTodoStore>>(StoreLogger);
        });
    }
}