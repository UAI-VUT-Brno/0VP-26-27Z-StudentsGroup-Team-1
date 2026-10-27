using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using UAI.ZeroVP.API.Controllers;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Chování REST kontroleru: status kódy, obsah odpovědí a dopad na úložiště.
/// Kontroler se volá přímo (bez HTTP) nad skutečným paměťovým repozitářem;
/// validaci vstupu obstarává ASP.NET Core ještě před kontrolerem, viz testy DTO.
/// </summary>
public sealed class TodosControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTodoRepository _repository = new(new InMemoryTodoOptions { SeedSampleData = false });
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private TodosController CreateController(ITodoRepository? repository = null) =>
        new(repository ?? _repository, _timeProvider, NullLogger<TodosController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private async Task<TodoItem> AddItemAsync(
        string title = "Úkol",
        TodoPriority priority = TodoPriority.Normal,
        bool isCompleted = false) =>
        await _repository.AddAsync(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            Priority = priority,
            IsCompleted = isCompleted,
            CreatedAtUtc = Now.AddDays(-1),
            CompletedAtUtc = isCompleted ? Now.AddHours(-1) : null,
            UpdatedAtUtc = Now.AddDays(-1)
        });

    /// <summary>Ověří odpověď 200 OK a vrátí její obsah.</summary>
    private static T AssertOk<T>(ActionResult<T> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<T>(ok.Value);
    }

    /// <summary>Ověří jednotnou odpověď 404 ve formátu ProblemDetails.</summary>
    private static void AssertNotFoundProblem(IActionResult? result, Guid id)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Contains(id.ToString(), problem.Detail);
    }

    // --- GET /api/todos -----------------------------------------------------

    [Fact]
    public async Task GetAll_ReturnsOkWithAllItemsAsDtos()
    {
        await AddItemAsync("Low", TodoPriority.Low);
        await AddItemAsync("High", TodoPriority.High);

        var items = AssertOk(await CreateController().GetAll(isCompleted: null, CancellationToken.None));

        Assert.Equal(["High", "Low"], items.Select(item => item.Title));
    }

    [Fact]
    public async Task GetAll_EmptyRepository_ReturnsOkWithEmptyList()
    {
        var items = AssertOk(await CreateController().GetAll(isCompleted: null, CancellationToken.None));

        Assert.Empty(items);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAll_CompletionFilter_IsPassedToRepository(bool isCompleted)
    {
        await AddItemAsync("Hotový", isCompleted: true);
        await AddItemAsync("Rozdělaný", isCompleted: false);

        var items = AssertOk(await CreateController().GetAll(isCompleted, CancellationToken.None));

        Assert.Equal(isCompleted, Assert.Single(items).IsCompleted);
    }

    // --- GET /api/todos/{id} ------------------------------------------------

    [Fact]
    public async Task GetById_ExistingItem_ReturnsOkWithDto()
    {
        var item = await AddItemAsync("Napsat Dockerfile");

        var dto = AssertOk(await CreateController().GetById(item.Id, CancellationToken.None));

        Assert.Equal(item.Id, dto.Id);
        Assert.Equal("Napsat Dockerfile", dto.Title);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNotFoundProblem()
    {
        var id = Guid.NewGuid();

        var result = await CreateController().GetById(id, CancellationToken.None);

        AssertNotFoundProblem(result.Result, id);
    }

    // --- POST /api/todos ----------------------------------------------------

    [Fact]
    public async Task Create_ReturnsCreatedWithRouteToNewItem()
    {
        var request = new CreateTodoItemRequest { Title = "Nový úkol", Priority = TodoPriority.High };

        var result = await CreateController().Create(request, CancellationToken.None);

        // Z CreatedAtRoute sestaví ASP.NET Core hlavičku Location: /api/todos/{id}.
        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(created.Value);
        Assert.Equal(nameof(TodosController.GetById), created.RouteName);
        Assert.Equal(dto.Id, created.RouteValues!["id"]);
        Assert.Equal("Nový úkol", dto.Title);
        Assert.Equal(TodoPriority.High, dto.Priority);
    }

    [Fact]
    public async Task Create_StoresItemStampedWithCurrentTime()
    {
        var request = new CreateTodoItemRequest { Title = "  Nový úkol  " };

        var result = await CreateController().Create(request, CancellationToken.None);

        var dto = Assert.IsType<TodoItemDto>(Assert.IsType<CreatedAtRouteResult>(result.Result).Value);
        var stored = await _repository.GetByIdAsync(dto.Id);
        Assert.NotNull(stored);
        Assert.Equal("Nový úkol", stored.Title);
        Assert.Equal(Now, stored.CreatedAtUtc);
        Assert.False(stored.IsCompleted);
    }
    
    [Fact]
    public async Task Create_ReturnsItemWithoutDueDate()
    {
        var result = await CreateController().Create(new CreateTodoItemRequest { Title = "Úkol" }, CancellationToken.None);

        var dto = Assert.IsType<TodoItemDto>(Assert.IsType<CreatedAtRouteResult>(result.Result).Value);
        Assert.Null(dto.DueAtUtc);
    }

    [Fact]
    public async Task Create_LineBreaksFromClient_DoNotReachTheLog()
    {
        // Konec řádku v názvu nebo userId by do textového logu podstrčil falešný řádek.
        var logger = new FakeLogger<TodosController>();
        var controller = new TodosController(_repository, _timeProvider, logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var request = new CreateTodoItemRequest
        {
            UserId = "jan\r\n2026-10-27 [ERR] falešný řádek",
            Title = "Úkol\nfalešný řádek"
        };

        await controller.Create(request, CancellationToken.None);

        var message = logger.LatestRecord.Message;
        Assert.DoesNotContain('\n', message);
        Assert.DoesNotContain('\r', message);
        Assert.Contains("Úkol falešný řádek", message);
    }

    // --- POST /api/todos/repeat ---------------------------------------------

    private static readonly DateTimeOffset FirstDue = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private static CreateRepeatingTodoItemRequest CreateRepeatingRequest(int repeatCount = 3) => new()
    {
        Title = "Týdenní report",
        Description = "Shrnout týden.",
        Priority = TodoPriority.High,
        FirstDueAtUtc = FirstDue,
        RepeatCount = repeatCount
    };

    /// <summary>Ověří odpověď 201 Created a vrátí vytvořené výskyty.</summary>
    private static IReadOnlyList<TodoItemDto> AssertCreatedOccurrences(ActionResult<IReadOnlyList<TodoItemDto>> result)
    {
        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(created.Value);
    }

    [Fact]
    public async Task CreateRepeating_ReturnsCreatedWithAllOccurrences()
    {
        var result = await CreateController().CreateRepeating(CreateRepeatingRequest(repeatCount: 3), CancellationToken.None);

        var occurrences = AssertCreatedOccurrences(result);
        Assert.Equal(
            [FirstDue, FirstDue.AddDays(7), FirstDue.AddDays(14), FirstDue.AddDays(21)],
            occurrences.Select(occurrence => occurrence.DueAtUtc));
        Assert.All(occurrences, occurrence =>
        {
            Assert.Equal("Týdenní report", occurrence.Title);
            Assert.Equal("Shrnout týden.", occurrence.Description);
            Assert.Equal(TodoPriority.High, occurrence.Priority);
            Assert.False(occurrence.IsCompleted);
            Assert.Equal(Now, occurrence.CreatedAtUtc);
        });
    }

    [Fact]
    public async Task CreateRepeating_LocationPointsToFirstOccurrence()
    {
        var result = await CreateController().CreateRepeating(CreateRepeatingRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        var occurrences = Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(created.Value);
        Assert.Equal(nameof(TodosController.GetById), created.RouteName);
        Assert.Equal(occurrences[0].Id, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task CreateRepeating_StoresEveryOccurrence()
    {
        var occurrences = AssertCreatedOccurrences(
            await CreateController().CreateRepeating(CreateRepeatingRequest(repeatCount: 52), CancellationToken.None));

        Assert.Equal(53, await _repository.CountAsync());
        foreach (var occurrence in occurrences)
        {
            var stored = await _repository.GetByIdAsync(occurrence.Id);
            Assert.NotNull(stored);
            Assert.Equal(occurrence.DueAtUtc, stored.DueAtUtc);
        }
    }

    [Fact]
    public async Task CreateRepeating_OccurrencesAreIndependentTasks()
    {
        // Výskyty nejsou pravidlo opakování: dokončení ani smazání jednoho se ostatních nedotkne.
        var controller = CreateController();
        var occurrences = AssertCreatedOccurrences(
            await controller.CreateRepeating(CreateRepeatingRequest(repeatCount: 2), CancellationToken.None));

        await controller.Complete(occurrences[0].Id, CancellationToken.None);
        await controller.Delete(occurrences[1].Id, CancellationToken.None);

        Assert.True((await _repository.GetByIdAsync(occurrences[0].Id))!.IsCompleted);
        Assert.Null(await _repository.GetByIdAsync(occurrences[1].Id));
        var untouched = await _repository.GetByIdAsync(occurrences[2].Id);
        Assert.NotNull(untouched);
        Assert.False(untouched.IsCompleted);
    }

    // --- PUT /api/todos/{id} ------------------------------------------------

    [Fact]
    public async Task Update_ExistingItem_ReturnsOkAndStoresChanges()
    {
        var item = await AddItemAsync("Původní");
        var request = new UpdateTodoItemRequest { Title = "Nový název", Priority = TodoPriority.High };

        var dto = AssertOk(await CreateController().Update(item.Id, request, CancellationToken.None));

        Assert.Equal("Nový název", dto.Title);
        var stored = await _repository.GetByIdAsync(item.Id);
        Assert.Equal("Nový název", stored!.Title);
        Assert.Equal(TodoPriority.High, stored.Priority);
        Assert.Equal(Now, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task Update_MarkAsCompleted_SetsCompletionTime()
    {
        var item = await AddItemAsync();
        var request = new UpdateTodoItemRequest { Title = "Úkol", IsCompleted = true };

        var dto = AssertOk(await CreateController().Update(item.Id, request, CancellationToken.None));

        Assert.True(dto.IsCompleted);
        Assert.Equal(Now, dto.CompletedAtUtc);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFoundAndDoesNotCreateItem()
    {
        var id = Guid.NewGuid();

        var result = await CreateController().Update(id, new UpdateTodoItemRequest { Title = "Úkol" }, CancellationToken.None);

        AssertNotFoundProblem(result.Result, id);
        Assert.Equal(0, await _repository.CountAsync());
    }

    [Fact]
    public async Task Update_ItemDeletedMeanwhile_ReturnsNotFound()
    {
        var item = await AddItemAsync();
        var controller = CreateController(new DeletedBeforeUpdateRepository(_repository));

        var result = await controller.Update(item.Id, new UpdateTodoItemRequest { Title = "Úkol" }, CancellationToken.None);

        AssertNotFoundProblem(result.Result, item.Id);
    }

    // --- POST /api/todos/{id}/complete --------------------------------------

    [Fact]
    public async Task Complete_OpenItem_ReturnsOkWithCompletionTime()
    {
        var item = await AddItemAsync(isCompleted: false);

        var dto = AssertOk(await CreateController().Complete(item.Id, CancellationToken.None));

        Assert.True(dto.IsCompleted);
        Assert.Equal(Now, dto.CompletedAtUtc);
        Assert.True((await _repository.GetByIdAsync(item.Id))!.IsCompleted);
    }

    [Fact]
    public async Task Complete_AlreadyCompletedItem_KeepsOriginalCompletionTime()
    {
        var item = await AddItemAsync(isCompleted: true);

        var dto = AssertOk(await CreateController().Complete(item.Id, CancellationToken.None));

        Assert.Equal(item.CompletedAtUtc, dto.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_UnknownId_ReturnsNotFoundProblem()
    {
        var id = Guid.NewGuid();

        var result = await CreateController().Complete(id, CancellationToken.None);

        AssertNotFoundProblem(result.Result, id);
    }

    [Fact]
    public async Task Complete_ItemDeletedMeanwhile_ReturnsNotFound()
    {
        var item = await AddItemAsync();
        var controller = CreateController(new DeletedBeforeUpdateRepository(_repository));

        var result = await controller.Complete(item.Id, CancellationToken.None);

        AssertNotFoundProblem(result.Result, item.Id);
    }

    // --- DELETE /api/todos/{id} ---------------------------------------------

    [Fact]
    public async Task Delete_ExistingItem_ReturnsNoContentAndRemovesIt()
    {
        var item = await AddItemAsync();

        var result = await CreateController().Delete(item.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _repository.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFoundProblem()
    {
        var id = Guid.NewGuid();

        var result = await CreateController().Delete(id, CancellationToken.None);

        AssertNotFoundProblem(result, id);
    }

    /// <summary>
    /// Napodobí souběh: úkol mezi načtením a uložením smaže jiný požadavek.
    /// Kontroler ho načte, ale <c>UpdateAsync</c> už vrátí <c>false</c>.
    /// </summary>
    private sealed class DeletedBeforeUpdateRepository(ITodoRepository inner) : ITodoRepository
    {
        public Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, CancellationToken cancellationToken = default) =>
            inner.GetAllAsync(isCompleted, cancellationToken);

        public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default) =>
            inner.AddAsync(item, cancellationToken);

        public Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default) =>
            inner.AddRangeAsync(items, cancellationToken);

        public async Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
        {
            await inner.DeleteAsync(item.Id, cancellationToken);
            return await inner.UpdateAsync(item, cancellationToken);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            inner.CountAsync(cancellationToken);
    }
}
