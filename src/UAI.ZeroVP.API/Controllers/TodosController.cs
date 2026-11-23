using System.ComponentModel.DataAnnotations;
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

    /// <summary>Vrátí seznam úkolů, volitelně filtrovaný podle uživatele a stavu dokončení.</summary>
    /// <remarks>Úkoly jsou seřazené od nejvyšší priority; při shodné prioritě od nejstaršího.</remarks>
    /// <param name="userId">Identifikátor uživatele; vynechání znamená bez filtru.</param>
    /// <param name="isCompleted">Filtr <c>true</c>/<c>false</c>; vynechání znamená bez filtru.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="200">Seznam úkolů seřazený podle priority a data vzniku.</response>
    /// <response code="400">Parametr <c>isCompleted</c> nemá hodnotu <c>true</c> ani <c>false</c>.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TodoItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<TodoItemDto>>> GetAll(
        [FromQuery] string? userId,
        [FromQuery] bool? isCompleted,
        CancellationToken cancellationToken)
    {
        var items = await _repository.GetAllAsync(
            userId: userId,
            isCompleted: isCompleted,
            cancellationToken: cancellationToken);

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
    /// Pole <c>version</c> musí obsahovat verzi, kterou klient naposledy četl. Pokud úkol mezitím
    /// změnil někdo jiný, vrátí se 409 a nic se nezapíše; po úspěšné změně se verze zvýší o 1.
    /// </remarks>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <param name="request">Nové hodnoty úkolu.</param>
    /// <response code="200">Aktualizovaný úkol s novou verzí.</response>
    /// <response code="400">Tělo požadavku není platný JSON nebo neprošlo validací (např. chybí <c>version</c> nebo není kladné celé číslo).</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    /// <response code="409">Verze v těle nesouhlasí s uloženou – úkol mezitím změnil někdo jiný. Nic se nezapsalo; <c>detail</c> uvádí aktuální verzi.</response>
    /// <response code="415">Tělo požadavku není ve formátu JSON (chybí hlavička <c>Content-Type: application/json</c>).</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<TodoItemDto>> Update(
        Guid id,
        [FromBody] UpdateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        // Validace [ApiController] chybějící verzi odmítne už dřív; tady jen pojistka,
        // aby chybějící verze nikdy nevedla k zápisu bez kontroly.
        var expectedVersion = request.Version
            ?? throw new ArgumentException("Verze úkolu chybí.", nameof(request));

        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFoundTodo(id);
        }

        request.ApplyTo(entity, _timeProvider.GetUtcNow());

        // Verzi porovnává až repozitář, v jednom kroku se zápisem. Porovnání tady
        // s právě načtenou entitou by nestačilo – jiná instance může zapsat mezi čtením a zápisem.
        var result = await _repository.UpdateIfVersionMatchesAsync(entity, expectedVersion, cancellationToken);

        return result switch
        {
            { Status: TodoWriteStatus.Success, Item: { } updated } => Ok(updated.ToDto()),
            { Status: TodoWriteStatus.Conflict, Item: { } current } => ConflictTodo(id, current.Version, expectedVersion),
            _ => NotFoundTodo(id)
        };
    }

    /// <summary>Označí úkol za dokončený.</summary>
    /// <remarks>
    /// U už dokončeného úkolu se nic nezmění; <c>completedAtUtc</c> zůstane původní.
    /// Verze úkolu se přesto zvýší o 1 (zvyšuje ji každý zápis) a odpověď ji obsahuje.
    /// </remarks>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="200">Úkol po změně stavu.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(TodoItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoItemDto>> Complete(Guid id, CancellationToken cancellationToken)
    {
        // Klient tu verzi neposílá, zápis se proto podmíní verzí, kterou jsme právě přečetli.
        // Když úkol mezitím změnil jiný požadavek, repozitář vrátí konflikt a úkol se načte znovu:
        // dokončí se jeho nejnovější stav a souběžná změna se nepřepíše. Každé další kolo znamená,
        // že jiný zápis uspěl, takže cyklus nemůže běžet donekonečna; ukončí ho i zrušení požadavku.
        while (true)
        {
            var entity = await _repository.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFoundTodo(id);
            }

            entity.SetCompletionState(isCompleted: true, _timeProvider.GetUtcNow());

            var result = await _repository.UpdateIfVersionMatchesAsync(entity, entity.Version, cancellationToken);
            if (result.Status != TodoWriteStatus.Conflict)
            {
                return result is { Status: TodoWriteStatus.Success, Item: { } completed }
                    ? Ok(completed.ToDto())
                    : NotFoundTodo(id);
            }
        }
    }

    /// <summary>Smaže úkol.</summary>
    /// <remarks>
    /// Úkol se smaže jen tehdy, když <c>version</c> odpovídá uložené verzi.
    /// Jinak by se smazala i změna, kterou klient ještě neviděl.
    /// </remarks>
    /// <param name="id">Identifikátor úkolu.</param>
    /// <param name="version">Verze úkolu, kterou klient naposledy četl. Povinná, kladné celé číslo.</param>
    /// <param name="cancellationToken">Token pro zrušení operace.</param>
    /// <response code="204">Úkol byl smazán.</response>
    /// <response code="400">Parametr <c>version</c> chybí nebo není kladné celé číslo.</response>
    /// <response code="404">Úkol se zadaným identifikátorem neexistuje.</response>
    /// <response code="409">Verze nesouhlasí s uloženou – úkol mezitím změnil někdo jiný. Nic se nesmazalo; <c>detail</c> uvádí aktuální verzi.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery]
        [Required(ErrorMessage = "Verze úkolu je povinná.")]
        [Range(1, long.MaxValue, ErrorMessage = "Verze úkolu musí být kladné celé číslo.")]
        long? version,
        CancellationToken cancellationToken)
    {
        // Stejná pojistka jako u Update: bez verze se nikdy nesmaže bez kontroly.
        var expectedVersion = version
            ?? throw new ArgumentException("Verze úkolu chybí.", nameof(version));

        var result = await _repository.DeleteIfVersionMatchesAsync(id, expectedVersion, cancellationToken);

        switch (result)
        {
            case { Status: TodoWriteStatus.Success }:
                _logger.LogInformation("Smazán úkol {TodoId}.", id);
                return NoContent();

            case { Status: TodoWriteStatus.Conflict, Item: { } current }:
                return ConflictTodo(id, current.Version, expectedVersion);

            default:
                return NotFoundTodo(id);
        }
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

    /// <summary>
    /// Jednotná odpověď 409 ve formátu ProblemDetails. <c>detail</c> uvádí aktuální verzi,
    /// aby klient poznal, že má úkol načíst znovu a změnu zopakovat nad novým stavem.
    /// </summary>
    private ObjectResult ConflictTodo(Guid id, long currentVersion, long expectedVersion) => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Konflikt verzí úkolu",
        detail: $"Úkol s identifikátorem {id} má aktuální verzi {currentVersion}, požadavek uváděl verzi {expectedVersion}. "
            + "Načtěte úkol znovu a změnu zopakujte.");
}
