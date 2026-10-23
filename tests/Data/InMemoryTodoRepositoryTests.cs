using Microsoft.Extensions.Time.Testing;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.Data;

/// <summary>
/// Paměťové úložiště úkolů: ukázková data, CRUD operace, filtrování a řazení,
/// ochrana uloženého stavu pomocí kopií a reakce na zrušení operace.
/// </summary>
public sealed class InMemoryTodoRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Prázdné úložiště – většina testů si data vkládá sama.</summary>
    private static InMemoryTodoRepository CreateEmptyRepository() =>
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private static TodoItem CreateItem(
        string title = "Úkol",
        TodoPriority priority = TodoPriority.Normal,
        bool isCompleted = false,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? dueAtUtc = null) => new()

    {
        Id = Guid.NewGuid(),
        Title = title,
        Priority = priority,
        IsCompleted = isCompleted,
        DueAtUtc = dueAtUtc,
        CreatedAtUtc = createdAtUtc ?? Now,
        UpdatedAtUtc = createdAtUtc ?? Now
    };

    // --- Ukázková data ------------------------------------------------------

    [Fact]
    public async Task Constructor_SeedEnabled_AddsFourSampleItems()
    {
        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions { SeedSampleData = true });

        Assert.Equal(4, await repository.CountAsync());
    }

    [Fact]
    public async Task Constructor_WithoutOptions_SeedsByDefault()
    {
        var repository = new InMemoryTodoRepository();

        Assert.Equal(4, await repository.CountAsync());
    }

    [Fact]
    public async Task Constructor_SeedDisabled_IsEmpty()
    {
        var repository = CreateEmptyRepository();

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task Constructor_Seed_UsesInjectedTimeProvider()
    {
        var timeProvider = new FakeTimeProvider(Now);

        var repository = new InMemoryTodoRepository(new InMemoryTodoOptions(), timeProvider);

        // Ukázkové úkoly vznikly "v minulosti" vůči podvrženému času, ne vůči skutečným hodinám.
        var items = await repository.GetAllAsync();
        Assert.All(items, item => Assert.InRange(item.CreatedAtUtc, Now.AddDays(-2), Now));
    }

    [Fact]
    public async Task Constructor_Seed_CompletedItemsHaveCompletionTime()
    {
        var repository = new InMemoryTodoRepository();

        var items = await repository.GetAllAsync();

        Assert.Contains(items, item => item.IsCompleted);
        Assert.All(items, item => Assert.Equal(item.IsCompleted, item.CompletedAtUtc is not null));
    }

    // --- Přidání ------------------------------------------------------------

    [Fact]
    public async Task AddAsync_NewItem_CanBeReadBack()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Napsat Dockerfile");

        await repository.AddAsync(item);

        var stored = await repository.GetByIdAsync(item.Id);
        Assert.NotNull(stored);
        Assert.Equal("Napsat Dockerfile", stored.Title);
    }

    [Fact]
    public async Task AddAsync_ReturnsCopyOfStoredItem()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Původní");

        var returned = await repository.AddAsync(item);
        returned.Title = "Změněno přes vrácenou instanci";

        Assert.NotSame(item, returned);
        Assert.Equal("Původní", (await repository.GetByIdAsync(item.Id))!.Title);
    }

    [Fact]
    public async Task AddAsync_ModifyingInputAfterwards_DoesNotChangeStoredItem()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Původní");

        await repository.AddAsync(item);
        item.Title = "Změněno přes vstupní instanci";

        Assert.Equal("Původní", (await repository.GetByIdAsync(item.Id))!.Title);
    }

    [Fact]
    public async Task AddAsync_DuplicateId_ThrowsInvalidOperationException()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();
        await repository.AddAsync(item);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(item.Clone()));
    }

    [Fact]
    public async Task AddAsync_Null_ThrowsArgumentNullException()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddAsync(null!));
    }
    
    // --- Přidání více úkolů najednou ----------------------------------------

    [Fact]
    public async Task AddRangeAsync_NewItems_AreAllStoredWithTheirDueDates()
    {
        var repository = CreateEmptyRepository();
        var items = Enumerable.Range(0, 4)
            .Select(week => CreateItem($"Týden {week}", dueAtUtc: Now.AddDays(7 * week)))
            .ToList();

        await repository.AddRangeAsync(items);

        Assert.Equal(4, await repository.CountAsync());
        foreach (var item in items)
        {
            var stored = await repository.GetByIdAsync(item.Id);
            Assert.NotNull(stored);
            Assert.Equal(item.Title, stored.Title);
            Assert.Equal(item.DueAtUtc, stored.DueAtUtc);
        }
    }

    [Fact]
    public async Task AddRangeAsync_ReturnsCopiesInInputOrder()
    {
        var repository = CreateEmptyRepository();
        var items = new[] { CreateItem("První"), CreateItem("Druhý"), CreateItem("Třetí") };

        var returned = await repository.AddRangeAsync(items);
        returned[0].Title = "Změněno přes vrácenou instanci";

        Assert.Equal(items.Select(item => item.Id), returned.Select(item => item.Id));
        Assert.All(returned.Zip(items), pair => Assert.NotSame(pair.Second, pair.First));
        Assert.Equal("První", (await repository.GetByIdAsync(items[0].Id))!.Title);
    }

    [Fact]
    public async Task AddRangeAsync_EmptyBatch_StoresNothing()
    {
        var repository = CreateEmptyRepository();

        Assert.Empty(await repository.AddRangeAsync([]));
        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddRangeAsync_IdAlreadyStored_ThrowsAndStoresNoneOfTheBatch()
    {
        var repository = CreateEmptyRepository();
        var existing = CreateItem("Už uložený");
        await repository.AddAsync(existing);

        // Kolize až u posledního prvku – první dva se stihnou vložit a musí se vrátit zpět.
        var batch = new[] { CreateItem(), CreateItem(), existing.Clone() };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddRangeAsync(batch));

        Assert.Equal(1, await repository.CountAsync());
        Assert.Null(await repository.GetByIdAsync(batch[0].Id));
        Assert.Null(await repository.GetByIdAsync(batch[1].Id));
    }

    [Fact]
    public async Task AddRangeAsync_DuplicateIdWithinBatch_ThrowsAndStoresNothing()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddRangeAsync([CreateItem(), item, item.Clone()]));

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddRangeAsync_NullBatch_ThrowsArgumentNullException()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddRangeAsync(null!));
    }

    [Fact]
    public async Task AddRangeAsync_NullItem_ThrowsAndStoresNothing()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<ArgumentException>(() => repository.AddRangeAsync([CreateItem(), null!]));

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task AddRangeAsync_CancelledToken_StoresNothing()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.AddRangeAsync([CreateItem(), CreateItem()], new CancellationToken(canceled: true)));

        Assert.Equal(0, await repository.CountAsync());
    }

    // --- Čtení jednoho úkolu ------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var repository = CreateEmptyRepository();

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdAsync_ModifyingResult_DoesNotChangeStoredItem()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Původní");
        await repository.AddAsync(item);

        var loaded = await repository.GetByIdAsync(item.Id);
        loaded!.Title = "Změněno bez UpdateAsync";

        Assert.Equal("Původní", (await repository.GetByIdAsync(item.Id))!.Title);
    }

    // --- Seznam, filtr a řazení ---------------------------------------------

    [Fact]
    public async Task GetAllAsync_EmptyRepository_ReturnsEmptyList()
    {
        var repository = CreateEmptyRepository();

        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_WithoutFilter_ReturnsAllItems()
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem(isCompleted: true));
        await repository.AddAsync(CreateItem(isCompleted: false));

        Assert.Equal(2, (await repository.GetAllAsync()).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAllAsync_CompletionFilter_ReturnsOnlyMatchingItems(bool isCompleted)
    {
        var repository = CreateEmptyRepository();
        await repository.AddAsync(CreateItem("Hotový", isCompleted: true));
        await repository.AddAsync(CreateItem("Rozdělaný", isCompleted: false));

        var items = await repository.GetAllAsync(isCompleted);

        var item = Assert.Single(items);
        Assert.Equal(isCompleted, item.IsCompleted);
    }

    [Fact]
    public async Task GetAllAsync_SortsByPriorityDescendingThenByCreationTime()
    {
        var repository = CreateEmptyRepository();

        // Vkládá se záměrně v "nesprávném" pořadí, aby řazení nemohlo projít náhodou.
        await repository.AddAsync(CreateItem("Low", TodoPriority.Low, createdAtUtc: Now.AddHours(-3)));
        await repository.AddAsync(CreateItem("High novější", TodoPriority.High, createdAtUtc: Now.AddHours(-1)));
        await repository.AddAsync(CreateItem("Normal", TodoPriority.Normal, createdAtUtc: Now.AddHours(-4)));
        await repository.AddAsync(CreateItem("High starší", TodoPriority.High, createdAtUtc: Now.AddHours(-2)));

        var titles = (await repository.GetAllAsync()).Select(item => item.Title);

        Assert.Equal(["High starší", "High novější", "Normal", "Low"], titles);
    }

    [Fact]
    public async Task GetAllAsync_ModifyingResult_DoesNotChangeStoredItems()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Původní");
        await repository.AddAsync(item);

        (await repository.GetAllAsync())[0].Title = "Změněno bez UpdateAsync";

        Assert.Equal("Původní", (await repository.GetByIdAsync(item.Id))!.Title);
    }

    // --- Aktualizace --------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ExistingItem_ReturnsTrueAndStoresChanges()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Původní");
        await repository.AddAsync(item);

        item.Title = "Nový název";
        item.Priority = TodoPriority.High;
        var updated = await repository.UpdateAsync(item);

        Assert.True(updated);
        var stored = await repository.GetByIdAsync(item.Id);
        Assert.Equal("Nový název", stored!.Title);
        Assert.Equal(TodoPriority.High, stored.Priority);
    }

    [Fact]
    public async Task UpdateAsync_UnknownItem_ReturnsFalseAndDoesNotAddIt()
    {
        var repository = CreateEmptyRepository();

        var updated = await repository.UpdateAsync(CreateItem());

        Assert.False(updated);
        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_ModifyingInputAfterwards_DoesNotChangeStoredItem()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem("Původní");
        await repository.AddAsync(item);

        item.Title = "Uloženo";
        await repository.UpdateAsync(item);
        item.Title = "Změněno po uložení";

        Assert.Equal("Uloženo", (await repository.GetByIdAsync(item.Id))!.Title);
    }

    [Fact]
    public async Task UpdateAsync_Null_ThrowsArgumentNullException()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.UpdateAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentUpdatesOfSameItem_AllSucceed()
    {
        // Souběžné zápisy nesmí vracet false (v API by to byla falešná 404) –
        // to hlídá cyklus kolem TryUpdate. Souběh nejde vynutit, test ho jen hodně zvýhodní.
        var repository = CreateEmptyRepository();
        var item = CreateItem();
        await repository.AddAsync(item);

        var results = await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() =>
        {
            var copy = item.Clone();
            copy.Title = $"Verze {i}";
            return repository.UpdateAsync(copy);
        })));

        Assert.All(results, Assert.True);
    }

    // --- Mazání a počet -----------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingItem_ReturnsTrueAndRemovesIt()
    {
        var repository = CreateEmptyRepository();
        var item = CreateItem();
        await repository.AddAsync(item);

        var deleted = await repository.DeleteAsync(item.Id);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        var repository = CreateEmptyRepository();

        Assert.False(await repository.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CountAsync_ReflectsAddsAndDeletes()
    {
        var repository = CreateEmptyRepository();
        var first = CreateItem();
        await repository.AddAsync(first);
        await repository.AddAsync(CreateItem());

        await repository.DeleteAsync(first.Id);

        Assert.Equal(1, await repository.CountAsync());
    }

    // --- Zrušení operace ----------------------------------------------------

    public static TheoryData<string, Func<InMemoryTodoRepository, CancellationToken, Task>> AllOperations => new()
    {
        { "GetAll", (repository, token) => repository.GetAllAsync(cancellationToken: token) },
        { "GetById", (repository, token) => repository.GetByIdAsync(Guid.NewGuid(), token) },
        { "Add", (repository, token) => repository.AddAsync(CreateItem(), token) },
        { "AddRange", (repository, token) => repository.AddRangeAsync([CreateItem()], token) },
        { "Update", (repository, token) => repository.UpdateAsync(CreateItem(), token) },
        { "Delete", (repository, token) => repository.DeleteAsync(Guid.NewGuid(), token) },
        { "Count", (repository, token) => repository.CountAsync(token) }
    };

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task AnyOperation_CancelledToken_ThrowsOperationCanceledException(
        string operation,
        Func<InMemoryTodoRepository, CancellationToken, Task> call)
    {
        _ = operation; // jen pro čitelný název případu ve výpisu testů
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<OperationCanceledException>(() => call(repository, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task AddAsync_CancelledToken_DoesNotStoreItem()
    {
        var repository = CreateEmptyRepository();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.AddAsync(CreateItem(), new CancellationToken(canceled: true)));

        Assert.Equal(0, await repository.CountAsync());
    }
}
