# Architecture rules

UAI.ZeroVP is a .NET 10 ToDo REST API in three projects. Dependencies point one way only:

```text
UAI.ZeroVP.DTO  ◄──  UAI.ZeroVP.Data  ◄──  UAI.ZeroVP.API
```

## Scope of a change

- Make the **smallest change that meets the acceptance criteria**. Do not refactor, rename or "improve" code that the issue does not need.
- Prefer reusing an existing method over adding a new one. For example, compute a count from `GetAllAsync` unless the issue asks for a dedicated repository operation.
- When the issue is ambiguous, choose the option that changes the fewest public contracts, and state the choice in the plan.

## Where code belongs

| Project | Put here | Never put here |
|---|---|---|
| **DTO** `src/UAI.ZeroVP.DTO` | Request and response contracts as `sealed record`, shared enums (`TodoPriority`), DataAnnotations validation, JSON converters for contract types | references to Data or API, entities, business logic |
| **Data** `src/UAI.ZeroVP.Data` | Entity `TodoItem`, `ITodoRepository`, its implementations, options classes, DI registration (`DataServiceCollectionExtensions`) | ASP.NET Core types, HTTP status codes, DTO mapping |
| **API** `src/UAI.ZeroVP.API` | Controllers, mapping entity ↔ DTO (`Mapping/TodoMapper.cs`), ProblemDetails responses, MCP tools (`Mcp/`), OpenAPI transformers, `Program.cs` configuration | storage logic, direct file access |

- Data may reference DTO only for shared enums such as `TodoPriority`.
- Controllers and MCP tools depend on `ITodoRepository` only, never on a concrete repository.
- Mapping is done with extension methods in `TodoMapper` (`ToDto`, `ToEntity`, `ApplyTo`). Do not map inside controllers.
- Internal entity fields (for example `UpdatedAtUtc`) never appear in a DTO.

## Repository changes

`ITodoRepository` has **two** implementations, and both must change together:

1. `InMemory/InMemoryTodoRepository.cs` contains the real logic (filtering, sorting, all-or-nothing batches).
2. `JsonFile/JsonFileTodoRepository.cs` wraps each operation in `ExecuteAsync(..., isWrite, ...)`. That method takes the file lock, loads the file into a fresh `InMemoryTodoRepository`, runs the operation and saves the file if `isWrite` is `true`.

When you add or change a repository method:

- add it to the interface, implement the logic in `InMemoryTodoRepository`, and delegate to it in `JsonFileTodoRepository` with the correct `isWrite` flag;
- keep every read-modify-write step inside **one** `ExecuteAsync` call, so that several API instances sharing the file stay consistent (see `docs/persistence.md` and `docs/multiple-instances.md`);
- give new parameters default values and put them after the existing ones, so existing callers still compile;
- update every caller in the **same commit** (see `repository.md`).

## API conventions

- Task endpoints live in `TodosController` under `api/todos`, and diagnostics live in `InfoController` under `api/info`. Use route constraints such as `{id:guid}`.
- Every action has XML docs with `<summary>`, `<param>` and one `<response code="...">` per status code, plus a matching `[ProducesResponseType]`.
- Errors use ProblemDetails:
  - 404 via the existing `NotFoundTodo(id)` helper;
  - other errors via `Problem(statusCode:, title:, detail:)`;
  - validation errors come automatically from `[ApiController]` as `ValidationProblemDetails`.
- Get the current time from the injected `TimeProvider`, never from `DateTime.Now` or `DateTimeOffset.UtcNow`.
- Pass client strings through `ForLog(...)` before you log them (log-forging protection).
- `201 Created` responses use `CreatedAtRoute(nameof(GetById), ...)`.
- JSON settings are configured once in `Program.cs` (`ConfigureJson`): enums as strings, strict numbers. Do not add per-endpoint serializer settings.

## Configuration

- New settings go in a configuration section, read in `Program.cs` with a default value. Example: `builder.Configuration.GetValue("Section:Key", default)`.
- Every new setting must also work as an environment variable with a double underscore (`Section__Key`), which is how Docker passes it.
- Document every new setting in `README.md` or the relevant file in `docs/`.

## MCP tools

- MCP tools in `src/UAI.ZeroVP.API/Mcp/TodoTools.cs` are read-only (`ReadOnly = true, Destructive = false`) unless the issue explicitly asks for writes.
- They return the same DTO shape as the REST API, serialized with the same JSON options.
