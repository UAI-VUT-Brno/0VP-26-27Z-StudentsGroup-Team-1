using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.JsonFile;

namespace UAI.ZeroVP.Data.DependencyInjection;

/// <summary>
/// Registrace datové vrstvy do kontejneru služeb.
/// API tak nemusí znát konkrétní implementaci úložiště – zná jen <see cref="ITodoRepository"/>.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje paměťové úložiště úkolů jako singleton.
    /// <para>
    /// Singleton je zde nutnost: data drží samotná instance repozitáře,
    /// takže scoped registrace by znamenala prázdné úložiště pro každý požadavek.
    /// </para>
    /// </summary>
    /// <param name="services">Kolekce služeb.</param>
    /// <param name="configure">Volitelná úprava nastavení úložiště.</param>
    public static IServiceCollection AddInMemoryTodoData(
        this IServiceCollection services,
        Action<InMemoryTodoOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new InMemoryTodoOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ITodoRepository>(sp => new InMemoryTodoRepository(
            sp.GetRequiredService<InMemoryTodoOptions>(),
            sp.GetService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Zaregistruje úložiště úkolů nad souborem JSON. Soubor se čte a zapisuje při každé
    /// operaci pod zámkem, takže ho může sdílet několik instancí API najednou.
    /// <para>
    /// Když soubor neexistuje nebo je poškozený, začíná se od nuly – s ukázkovými daty,
    /// pokud jsou v <see cref="InMemoryTodoOptions"/> zapnutá.
    /// </para>
    /// </summary>
    /// <param name="services">Kolekce služeb.</param>
    /// <param name="configureStorage">Nastavení souboru; cesta musí být vyplněná.</param>
    /// <param name="configure">Volitelná úprava nastavení paměťového úložiště.</param>
    public static IServiceCollection AddJsonFileTodoData(
        this IServiceCollection services,
        Action<JsonFileTodoOptions> configureStorage,
        Action<InMemoryTodoOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureStorage);

        var storageOptions = new JsonFileTodoOptions();
        configureStorage(storageOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageOptions.FilePath, nameof(configureStorage));

        var options = new InMemoryTodoOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(storageOptions);
        services.AddSingleton(sp => new JsonFileTodoStore(
            sp.GetRequiredService<JsonFileTodoOptions>(),
            sp.GetService<ILogger<JsonFileTodoStore>>() ?? NullLogger<JsonFileTodoStore>.Instance,
            sp.GetService<TimeProvider>()));

        // Singleton kvůli zámku uvnitř procesu: všechny požadavky musí čekat na tentýž.
        services.AddSingleton<ITodoRepository>(sp => new JsonFileTodoRepository(
            sp.GetRequiredService<JsonFileTodoStore>(),
            sp.GetRequiredService<JsonFileTodoOptions>(),
            sp.GetRequiredService<InMemoryTodoOptions>(),
            sp.GetService<TimeProvider>()));

        return services;
    }
}
