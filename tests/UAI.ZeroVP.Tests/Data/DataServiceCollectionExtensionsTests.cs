using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.InMemory;

namespace UAI.ZeroVP.Tests.Data;

/// <summary>
/// Registrace datové vrstvy do DI kontejneru. Ověřuje se výsledný kontejner,
/// tedy to, co skutečně dostane kontroler – ne jen seznam registrací.
/// </summary>
public sealed class DataServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryTodoData_RegistersInMemoryRepository()
    {
        using var provider = new ServiceCollection().AddInMemoryTodoData().BuildServiceProvider();

        Assert.IsType<InMemoryTodoRepository>(provider.GetRequiredService<ITodoRepository>());
    }

    [Fact]
    public void AddInMemoryTodoData_RepositoryIsSharedAcrossScopes()
    {
        // Singleton je nutnost: data drží instance repozitáře. Kdyby byl scoped,
        // každý HTTP požadavek (= scope) by dostal vlastní prázdné úložiště.
        using var provider = new ServiceCollection().AddInMemoryTodoData().BuildServiceProvider();

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<ITodoRepository>(),
            secondScope.ServiceProvider.GetRequiredService<ITodoRepository>());
    }

    [Fact]
    public async Task AddInMemoryTodoData_DataSurviveBetweenScopes()
    {
        using var provider = new ServiceCollection()
            .AddInMemoryTodoData(options => options.SeedSampleData = false)
            .BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITodoRepository>().AddAsync(new()
            {
                Id = Guid.NewGuid(),
                Title = "Úkol z prvního požadavku",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        using (var scope = provider.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ITodoRepository>().CountAsync());
        }
    }

    [Fact]
    public async Task AddInMemoryTodoData_WithoutConfigure_SeedsSampleData()
    {
        using var provider = new ServiceCollection().AddInMemoryTodoData().BuildServiceProvider();

        Assert.Equal(4, await provider.GetRequiredService<ITodoRepository>().CountAsync());
    }

    [Fact]
    public async Task AddInMemoryTodoData_ConfigureDisablesSeed_RepositoryIsEmpty()
    {
        using var provider = new ServiceCollection()
            .AddInMemoryTodoData(options => options.SeedSampleData = false)
            .BuildServiceProvider();

        Assert.Equal(0, await provider.GetRequiredService<ITodoRepository>().CountAsync());
    }

    [Fact]
    public void AddInMemoryTodoData_RegistersConfiguredOptions()
    {
        using var provider = new ServiceCollection()
            .AddInMemoryTodoData(options => options.SeedSampleData = false)
            .BuildServiceProvider();

        Assert.False(provider.GetRequiredService<InMemoryTodoOptions>().SeedSampleData);
    }

    [Fact]
    public async Task AddInMemoryTodoData_UsesRegisteredTimeProvider()
    {
        var now = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        using var provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(now))
            .AddInMemoryTodoData()
            .BuildServiceProvider();

        // Ukázková data jsou datovaná podle času z DI, ne podle skutečných hodin.
        var items = await provider.GetRequiredService<ITodoRepository>().GetAllAsync();
        Assert.All(items, item => Assert.InRange(item.CreatedAtUtc, now.AddDays(-2), now));
    }

    [Fact]
    public void AddInMemoryTodoData_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddInMemoryTodoData());
    }

    [Fact]
    public void AddInMemoryTodoData_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DataServiceCollectionExtensions.AddInMemoryTodoData(null!));
    }
}
