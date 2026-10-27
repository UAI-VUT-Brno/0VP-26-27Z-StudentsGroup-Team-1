using Microsoft.AspNetCore.Mvc;
using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Controllers;

/// <summary>
/// REST rozhraní nad seznamem úkolů.
/// Kontroler zná pouze rozhraní <see cref="ITodoRepository"/> – nikoli to,
/// že za ním stojí paměťové úložiště.
/// </summary>
[ApiController]
[Route("api/todos")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Neočekávaná chyba serveru.")]
public sealed class TodosController : ControllerBase
{
    private readonly ITodoRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TodosController> _logger;

    /// <summary>Vytvoří kontroler se závislostmi z DI kontejneru.</summary>
    public TodosController(ITodoRepository repository, TimeProvider timeProvider, ILogger<TodosController> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Vrátí seznam úkolů, volitelně filtrovaný podle stavu dokončení.</summary>
    /// <remarks>Úkoly jsou seřazené od nejvyšší priority; při shodné prioritě od nejstaršího.</remarks>
    /// <param name="isCompleted">Filtr <c>true</c>/<c>false</c>; vynechání znamená bez filtru.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="200">Seznam úkolů seřazený podle priority a data vzniku.</response>
    /// <response code="400">Parametr <c>isCompleted</c> nemá hodnotu <c>true</c> ani <c>false</c>.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TodoItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<TodoItemDto>>> GetAll(
        [FromQuery] bool? isCompleted,
        CancellationToken cancellationToken)
    {
        var items = await _repository.GetAllAsync(isCompleted: isCompleted, cancellationToken: cancellationToken);

        return Ok(items.Select(item => item.ToDto()).ToList());
    }

    /// <summary>Vrátí jeden úkol podle identifikátoru.</summary>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="200">Nalezený úkol.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpGet("{id:guid}", Name = nameof(GetById))]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoItemDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);

        return item is null ? NotFoundTodo(id) : Ok(item.ToDto());
    }

    /// <summary>Založí nový úkol.</summary>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <param name="request">Údaje nového úkolu.</param>
    /// <response code="201">Úkol byl vytvořen; hlavička <c>Location</c> odkazuje na nový zdroj.</response>
    /// <response code="400">Tělo požadavku není platný JSON nebo neprošlo validací.</response>
    /// <response code="415">Tělo požadavku není ve formátu JSON (chybí hlavička <c>Content-Type: application/json</c>).</response>
    [HttpPost]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<TodoItemDto>> Create(
        [FromBody] CreateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var entity = request.ToEntity(_timeProvider.GetUtcNow());
        var created = await _repository.AddAsync(entity, cancellationToken);

        _logger.LogInformation(
            "Vytvořen úkol {TodoId} ({Title}) pro uživatele {UserId}.",
            created.Id, ForLog(created.Title), ForLog(created.UserId));

        return CreatedAtRoute(nameof(GetById), new { id = created.Id }, created.ToDto());
    }

    /// <summary>Založí úkol, který se opakuje každý týden.</summary>
    /// <remarks>
    /// Vznikne první výskyt a <c>repeatCount</c> dalších, každý o sedm dní později.
    /// Výskyty jsou samostatné úkoly: dokončení nebo smazání jednoho se ostatních nedotkne.
    /// Při chybě ve vstupu se nevytvoří žádný.
    /// </remarks>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <param name="request">Údaje opakovaného úkolu a počet opakování.</param>
    /// <response code="201">Výskyty byly vytvořeny, seřazené podle termínu; hlavička <c>Location</c> odkazuje na první z nich.</response>
    /// <response code="400">Tělo požadavku není platný JSON nebo neprošlo validací; nevznikl žádný úkol.</response>
    /// <response code="415">Tělo požadavku není ve formátu JSON (chybí hlavička <c>Content-Type: application/json</c>).</response>
    [HttpPost("repeat")]
    [ProducesResponseType(typeof(IReadOnlyList<TodoItemDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<IReadOnlyList<TodoItemDto>>> CreateRepeating(
        [FromBody] CreateRepeatingTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var entities = request.ToEntities(_timeProvider.GetUtcNow());
        var created = await _repository.AddRangeAsync(entities, cancellationToken);

        _logger.LogInformation(
            "Vytvořen opakovaný úkol ({Title}) pro uživatele {UserId} s {OccurrenceCount} výskyty od {FirstDueAtUtc}.",
            ForLog(created[0].Title), ForLog(created[0].UserId), created.Count, created[0].DueAtUtc);

        return CreatedAtRoute(
            nameof(GetById),
            new { id = created[0].Id },
            created.Select(item => item.ToDto()).ToList());
    }


    /// <summary>Přepíše existující úkol novými hodnotami.</summary>
    /// <remarks>
    /// Jde o úplnou aktualizaci: hodnota, která v těle chybí, se nastaví na výchozí
    /// (<c>description</c> na <c>null</c>, <c>priority</c> na <c>Normal</c>, <c>isCompleted</c> na <c>false</c>).
    /// Uživatel, kterému úkol patří (<c>userId</c>), se nemění.
    /// </remarks>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <param name="request">Nové hodnoty úkolu.</param>
    /// <response code="200">Aktualizovaný úkol.</response>
    /// <response code="400">Tělo požadavku není platný JSON nebo neprošlo validací.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    /// <response code="415">Tělo požadavku není ve formátu JSON (chybí hlavička <c>Content-Type: application/json</c>).</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<TodoItemDto>> Update(
        Guid id,
        [FromBody] UpdateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFoundTodo(id);
        }

        request.ApplyTo(entity, _timeProvider.GetUtcNow());

        return await _repository.UpdateAsync(entity, cancellationToken)
            ? Ok(entity.ToDto())
            : NotFoundTodo(id);
    }

    /// <summary>Označí úkol za dokončený.</summary>
    /// <remarks>U už dokončeného úkolu se nic nezmění; <c>completedAtUtc</c> zůstane původní.</remarks>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="200">Úkol po změně stavu.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoItemDto>> Complete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFoundTodo(id);
        }

        entity.SetCompletionState(isCompleted: true, _timeProvider.GetUtcNow());

        return await _repository.UpdateAsync(entity, cancellationToken)
            ? Ok(entity.ToDto())
            : NotFoundTodo(id);
    }

    /// <summary>Smaže úkol.</summary>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="204">Úkol byl smazán.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await _repository.DeleteAsync(id, cancellationToken))
        {
            return NotFoundTodo(id);
        }

        _logger.LogInformation("Smazán úkol {TodoId}.", id);

        return NoContent();
    }

    /// <summary>
    /// Hodnota od klienta upravená pro zápis do logu: konce řádků se nahradí mezerou.
    /// Jinak by klient mohl v názvu nebo <c>userId</c> poslat "\n" a do textového logu
    /// tak podstrčit falešný řádek (log forging, CodeQL <c>cs/log-forging</c>).
    /// </summary>
    private static string? ForLog(string? value) =>
        value?.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>Jednotná odpověď 404 ve formátu ProblemDetails.</summary>
    private ObjectResult NotFoundTodo(Guid id) => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Úkol nenalezen",
        detail: $"Úkol s identifikátorem {id} neexistuje.");
}
