# Plan for #53: 0VP-T1-20 Benchmark: Add user identification to tasks

## Goal
Every task gets an owner identifier `userId`, a plain string with no authentication. Both create endpoints require and validate it, it is saved in the JSON file and every task response returns it. The owner is set at creation and cannot be changed later. Tasks from older files without `userId` keep working.

## Acceptance criteria
1. `POST /api/todos` and `POST /api/todos/repeat` accept `userId` and return it in the response; every repeated occurrence carries it.
2. Invalid `userId` returns `400` and creates nothing.
3. `GET` endpoints return `userId`.
4. Unit tests for validation, mapping and the repository; API tests over HTTP.
5. `userId` is required, trimmed and 1–100 characters long. Missing, `null`, empty and whitespace-only values are rejected. The value is stored trimmed and keeps its letter case. (derived)
6. `PUT /api/todos/{id}` cannot change the owner. `UpdateTodoItemRequest` has no `userId`, a `userId` sent in a PUT body is ignored, and the stored owner stays the same. `POST /complete` also keeps it. (derived)
7. `userId` is saved in the JSON file. Tasks loaded from an older file without `userId` (or with `"userId": null`) load with a `null` owner, are returned by the API, and are still there after the next write. (derived)
8. The OpenAPI document shows `userId` as required with `minLength` 1 and `maxLength` 100 in both create request schemas, with an `<example>`. It shows `userId` as a required nullable string in `TodoItemDto` and does not list it in `UpdateTodoItemRequest`. The README and docs are updated. (derived)

## Decisions
- **Scope: 23 files. 22 existing files change and 1 new test file is created.** By area: DTO 3, Data 1, API 3, tests 12 (1 new), docs 4.
- **Owner on update:** `UpdateTodoItemRequest` does not change, so the owner is fixed at creation, as the issue proposes. This changes the fewest public contracts. A `userId` in a PUT body is ignored, because System.Text.Json skips unknown members by default and nothing in `Program.cs` or the DTOs turns that off. Rejecting it with `400` would need a new property and a new validation rule. `TodoItem.UserId` uses `init`, so `ApplyTo` cannot change it by mistake.
- **Validation rules:** use the same pattern as `Title`. Both create requests get `public string UserId { get; init; } = string.Empty;` with `[Required(AllowEmptyStrings = false, ErrorMessage = "Identifikátor uživatele je povinný.")]` and `[StringLength(100, MinimumLength = 1, ErrorMessage = "Identifikátor uživatele musí mít 1 až 100 znaků.")]`.
  - There is no C# `required` keyword, for the same reason as `Title`: a missing field must reach the `[Required]` message instead of failing during deserialization.
  - Trimming happens in `TodoMapper.ToEntity`.
  - The length is checked on the raw value, as it is for `Title`.
  - Letter case is kept and the value is not normalised. Future filters should compare with `StringComparison.Ordinal`.
- **Older JSON files:** the entity property is `public string? UserId { get; init; }`, nullable and not `required`.
  - A file without the field loads with `null`.
  - `"userId": null` passes `RespectNullableAnnotations` in `JsonFileTodoStore`. A save writes that value for older tasks once any write rewrites the file.
  - `TodoItemDto.UserId` is `required string?`: always sent, and `null` for older tasks. This follows the existing `Description` pattern. `null` is not mapped to `""`, which keeps the mapper free of special cases.
- **Repository:** no repository change. `ITodoRepository`, `InMemoryTodoRepository` and `JsonFileTodoRepository` keep their signatures and logic. The field moves through `TodoItem.Clone()`, and the `JsonFileTodoStore` serializer picks it up automatically.
- **Sample data:** the four seeded tasks in `InMemoryTodoRepository.Seed()` keep a `null` owner, so `Seed` does not change.
- **Out of scope:** filtering by `userId`, logging `userId`, and any authentication or access control. There are no MCP tools in `src/UAI.ZeroVP.API/Mcp/`, so nothing changes there.
- **README language:** additions to `README.md` are written in Czech, because the existing file is Czech. The rules say docs are in English, but where a rule and the code disagree the code wins (`rules/README.md`). The files in `docs/` stay in English.
- **New test class:** the new HTTP tests go into their own class, `tests/API/TodoUserIdEndpointTests.cs`, instead of being added to `RepeatingTodosEndpointTests`, which is about `/repeat`.
- **Commit order (each commit must build and keep the suite green):**
  1. `feat(dto)`: the two create requests, plus the request data in existing tests that now needs `userId` (DTO tests, `RepeatingTodosEndpointTests`, `OpenApiDocumentTests`, `ApiResponseTimeTests`), plus the new DTO tests.
  2. `feat(data)`: `TodoItem` and the Data tests.
  3. `feat(api)`: `TodoItemDto.UserId` together with `TodoMapper`, because a `required` member breaks `ToDto` unless both change in one commit. Also the controller XML docs, the `.http` file, and the API and OpenAPI tests.
  4. `docs(api)`: README and docs.

## Changes by layer
### DTO (`src/UAI.ZeroVP.DTO`)
- `src/UAI.ZeroVP.DTO/CreateTodoItemRequest.cs`: add `UserId` with the attributes from *Decisions*.
  - XML `<summary>` (Czech): "Identifikátor uživatele, kterému úkol patří (jméno, ID nebo e-mail). Povinný, 1–100 znaků, ořízne se. Zatím bez ověřování."
  - `<remarks>` explaining why there is no `required` keyword (same text as on `Title`).
  - `<example>jan.novak</example>`.
- `src/UAI.ZeroVP.DTO/CreateRepeatingTodoItemRequest.cs`: add the same `UserId` property, attributes, docs and example. Extend the class `<remarks>` so the rules for "název, popis, prioritu a vlastníka" must match `CreateTodoItemRequest`.
- `src/UAI.ZeroVP.DTO/TodoItemDto.cs`: add `public required string? UserId { get; init; }` after `CompletedAtUtc`.
  - `<summary>`: the owner, or `null` for tasks saved before owners existed.
  - This must ship in the same commit as the `TodoMapper.ToDto` change (see *Decisions*).
- `UpdateTodoItemRequest.cs`: none.

### Data (`src/UAI.ZeroVP.Data`)
- `src/UAI.ZeroVP.Data/Entities/TodoItem.cs`:
  - Add `public string? UserId { get; init; }` after `CompletedAtUtc`.
  - The Czech XML doc explains why it is `init` (the owner is fixed at creation) and why it is nullable and not `required`: older files have no `userId`, and `required` would make `JsonFileTodoStore` treat them as corrupted, move them aside and start empty.
  - Copy `UserId = UserId` in `Clone()`.
- `ITodoRepository`, `InMemoryTodoRepository`, `JsonFileTodoRepository`, `JsonFileTodoStore`: none, because no method or signature changes.

### API (`src/UAI.ZeroVP.API`)
- `src/UAI.ZeroVP.API/Mapping/TodoMapper.cs`:
  - `ToDto`: set `UserId = entity.UserId`.
  - `ToEntity`: set `UserId = request.UserId.Trim()`.
  - `ToEntities`: add `UserId = request.UserId` to the `template` `CreateTodoItemRequest`, so every occurrence gets the trimmed owner through `ToEntity`.
  - `ApplyTo`: no code change. Extend its `<summary>` to say the owner is not changed because it is fixed at creation.
- `src/UAI.ZeroVP.API/Controllers/TodosController.cs`: XML docs only.
  - Add a `<remarks>` to `Create` saying `userId` is required (1–100 characters, trimmed, not verified).
  - Extend the `CreateRepeating` `<remarks>`: every occurrence gets the same `userId`.
  - Extend the `Update` `<remarks>`: `userId` cannot be changed and a value in the body is ignored.
  - Do not change the text or the position of `<param name="request">`, because `RequestBody_IsDescribedByTheRequestParameter` checks it.
- `src/UAI.ZeroVP.API/UAI.ZeroVP.API.http`:
  - Add `"userId": "jan.novak"` to the sample POST bodies ("Nový úkol", "Neplatný vstup", the two `/repeat` samples), so each sample still shows the result it describes.
  - Add one sample "Neplatný userId – vrátí 400" with `"userId": "   "`.

## Tests (`tests/`)
- **Existing tests that need `userId` in their request data.** The issue explicitly makes `userId` required, so these changes are data only and no assertion changes. Say so in the commit message and the PR.
  - `tests/DTO/CreateTodoItemRequestTests.cs`:
    - Add a private const `ValidUserId = "jan.novak"`.
    - Add `UserId = ValidUserId` to every request that is validated (the initializers in `Validate_TitleOnly_IsValid`, `Validate_MissingTitle_FailsOnTitle`, `Validate_TitleWithinLimit_IsValid`, `Validate_TitleOverLimit_FailsOnTitle`, `Validate_OptionalDescription_IsValid`, `Validate_DescriptionAtLimit_IsValid`, `Validate_DescriptionOverLimit_FailsOnDescription`, `Validate_DefinedPriority_IsValid`, `Validate_UndefinedPriority_FailsOnPriority`).
    - Rename `Validate_TitleOnly_IsValid` to `Validate_RequiredFieldsOnly_IsValid`, because a title alone is no longer valid.
  - `tests/DTO/CreateRepeatingTodoItemRequestTests.cs`: add `UserId = "jan.novak"` to `CreateValidRequest()`.
  - `tests/API/RepeatingTodosEndpointTests.cs`:
    - Add `"userId":"jan.novak"` to `ValidBody`, to every body in `InvalidRequests` (so each case still fails only on its own field) and to the body in `PostTodo_WithoutDueDate_ReturnsDueAtUtcNull`.
  - `tests/OpenApi/OpenApiDocumentTests.cs`: add `"userId"` to `RepeatBody` and to the `body` in `SuccessfulTodoLifecycle_MatchesDocument`. That body is also sent to PUT, where `userId` is ignored and the request still returns `200`.
  - `tests/Performance/ApiResponseTimeTests.cs`: add `UserId = "zatezovy.test"` to the `CreateTodoItemRequest` in `WriteScenarioAsync`. The test stays skipped by default, but it would get `400` when `RUN_PERFORMANCE_TESTS=true`.
  - `tests/Data/TodoItemTests.cs`: add `UserId = "jan.novak"` to `CreateFullyPopulatedItem()`. Without it, the existing guard `Clone_CopiesEveryPublicProperty` fails on purpose; with it, that test also covers `Clone()` → AC 4, 7.
  - `tests/Data/JsonFileTodoStoreTests.cs`: add `UserId = "jan.novak"` to the item in `SaveThenLoad_ReturnsAllFields`, which claims to cover all fields → AC 7.
- **DTO validation:**
  - `tests/DTO/CreateTodoItemRequestTests.cs`:
    - `Validate_MissingUserId_FailsOnUserId` (Theory: `null`, `""`, `"   "`) → AC 2, 5
    - `Validate_UserIdWithinLimit_IsValid` (Theory: lengths 1 and 100) → AC 5
    - `Validate_UserIdOverLimit_FailsOnUserId` (length 101) → AC 2, 5
  - `tests/DTO/CreateRepeatingTodoItemRequestTests.cs`:
    - `Validate_MissingUserId_FailsOnUserId` (Theory: `null`, `""`, `"   "`) → AC 2, 5
    - `Validate_UserIdWithinLimit_IsValid` (Theory: lengths 1 and 100) → AC 5
    - `Validate_UserIdOverLimit_FailsOnUserId` → AC 2, 5
- **Repository, in-memory implementation** (`tests/Data/InMemoryTodoRepositoryTests.cs`). Add an optional `string? userId = null` as the last parameter of the `CreateItem` helper, then:
  - `AddAsync_ItemWithUserId_StoresUserId` → AC 1, 4
  - `AddRangeAsync_EveryItemKeepsItsUserId` → AC 1, 4
  - `UpdateAsync_KeepsUserId` → AC 6
  - `Constructor_InitialItemsWithoutUserId_AreLoadedWithNullUserId` → AC 7
- **JSON file store** (`tests/Data/JsonFileTodoStoreTests.cs`):
  - `Save_WritesUserId`: the file contains `"userId": "jan.novak"` → AC 7
  - `Load_FileWithoutUserId_LoadsTasksWithNullUserId`: a literal JSON file in the old format (`id`, `title`, `description`, `priority`, `isCompleted`, `dueAtUtc`, `createdAtUtc`, `completedAtUtc`, `updatedAtUtc`). It must load, return `null` and not be moved aside as corrupted → AC 7
  - `Load_UserIdNull_LoadsTaskWithNullUserId` → AC 7
- **JSON file repository** (`tests/Data/JsonFileTodoRepositoryTests.cs`):
  - `Add_WithUserId_IsVisibleThroughAnotherInstance` → AC 1, 4, 7
  - `LegacyFileWithoutUserId_TasksSurviveAWrite`: write an old-format file, `AddAsync` a new item with a `userId`, then `GetAllAsync` returns both. The old task has `null` and the new one keeps its owner → AC 7
- **Mapping** (`tests/API/TodoMapperTests.cs`). Add `UserId = "jan.novak"` to `CreateEntity` and `UserId = "  jan.novak  "` to `CreateRepeatingRequest`, then:
  - `ToDto_CopiesUserId` → AC 3
  - `ToDto_NullUserId_StaysNull` → AC 7
  - `ToEntity_TrimsUserId` → AC 1, 5
  - `ToEntity_KeepsUserIdCase` (`"Jan.Novak"` stays as it is) → AC 5
  - `ToEntities_CopiesTrimmedUserIdToEveryOccurrence` → AC 1
  - `ApplyTo_KeepsUserId` → AC 6
- **Controller unit tests** (`tests/API/TodosControllerTests.cs`). Add an optional `string? userId = null` parameter to `AddItemAsync` and `UserId = "jan.novak"` to `CreateRepeatingRequest`, then:
  - `Create_ReturnsAndStoresUserId` → AC 1
  - `CreateRepeating_EveryOccurrenceCarriesUserId` → AC 1
  - `GetAll_ReturnsUserId` → AC 3
  - `GetById_ReturnsUserId` → AC 3
  - `Update_KeepsOriginalUserId` → AC 6
  - `Complete_KeepsUserId` → AC 6
- **HTTP tests** (new file `tests/API/TodoUserIdEndpointTests.cs`, namespace `UAI.ZeroVP.Tests.API`, `sealed class TodoUserIdEndpointTests : IClassFixture<ApiTestFactory>, IDisposable`, with a temp directory deleted in `Dispose`):
  - `PostTodo_WithUserId_Returns201WithTrimmedUserId`: status, `Content-Type: application/json` and body `userId` → AC 1, 5
  - `PostRepeat_WithUserId_EveryOccurrenceCarriesUserId` → AC 1
  - `Post_InvalidUserId_Returns400ValidationProblemAndCreatesNothing`:
    - Theory over both URLs. Cases: field missing, `null`, `""`, `"   "` and 101 characters give errors key `UserId`; the number `123` gives `$.userId`.
    - `todoCount` from `/api/info` stays the same → AC 2
  - `Post_UserIdAtLimit_Returns201` (100 characters) → AC 5
  - `GetById_ReturnsUserId` → AC 3
  - `GetAll_ReturnsUserId` → AC 3
  - `Put_WithDifferentUserId_KeepsOriginalOwner` → AC 6
  - `Get_TaskFromFileWithoutUserId_ReturnsNullUserId`: write an old-format file, then `_factory.WithWebHostBuilder(b => b.UseSetting("Todo:Storage:FilePath", path))`. `GET /api/todos/{id}` and `GET /api/todos` return `200` with `"userId": null` → AC 3, 7
- **OpenAPI** (`tests/OpenApi/OpenApiDocumentTests.cs`):
  - `UserId_IsDocumentedAsRequiredWithLengthLimits` (Theory: `CreateTodoItemRequest`, `CreateRepeatingTodoItemRequest`; checks `Required` contains `userId`, `MinLength == 1`, `MaxLength == 100`) → AC 8
  - `TodoItemDto_DocumentsUserIdAsRequired` → AC 3, 8
  - `UpdateRequest_DoesNotDocumentUserId` → AC 6, 8

## Documentation
- `README.md` (Czech, to match the file):
  - Endpoint table: the two POST rows mention the required `userId`, and the PUT row says the owner cannot be changed.
  - New subsection "Vlastník úkolu (`userId`)": required, trimmed, 1–100 characters, any string (name, ID or e-mail), no authentication or access control, fixed at creation, `null` for tasks from older files.
  - Add `userId` to the list of fields in "Týdně se opakující úkol" and to the `curl` bodies in "Rychlý test".
- `docs/openapi.md`:
  - "Documented endpoints": note that `CreateTodoItemRequest` and `CreateRepeatingTodoItemRequest` require `userId` (1–100 characters), that `TodoItemDto.userId` is a string or `null`, and that `UpdateTodoItemRequest` has no `userId`.
  - Add the three new tests to the *Tests* table.
- `docs/persistence.md`:
  - Add `"userId": "jan.novak"` to the *File format* example.
  - New paragraph: files written before `userId` existed load with `null`, the next write saves `"userId": null`, and `userId` is not a required field (the list of corruption conditions stays the same).
  - Warn that all instances sharing one file must be upgraded together.
  - Update the *Tests* table rows for `JsonFileTodoStoreTests` and `JsonFileTodoRepositoryTests`.
- `docs/multiple-instances.md`: add `"userId"` to the example `curl` body (`'{"title":"Created through 5001"}'`), which would otherwise return `400`.

## Risks
- **Breaking change for clients:** a POST without `userId` now gets `400`. The issue asks for this. Existing tests only get `userId` added to their request data and no assertion changes; the PR must say so.
- **Data loss if the entity is declared wrongly:**
  - If `TodoItem.UserId` were `required`, every older file would be treated as corrupted, renamed to `*.corrupt-*`, and the API would start empty without warning.
  - If it were a non-nullable `string`, a `"userId": null` written after an older task is saved again would also be rejected.
  - It must stay `string?` and not `required`. `Load_FileWithoutUserId_LoadsTasksWithNullUserId` and `Load_UserIdNull_LoadsTaskWithNullUserId` guard this.
- **Build order:** `TodoItemDto.UserId` is a `required` member, so it must land in the same commit as `TodoMapper.ToDto`. Otherwise that commit fails with CS9035.
- **Instances on different versions sharing one file:** an older instance ignores `userId` when it reads the file and rewrites the whole file on its next write. That would remove `userId` from every task. All instances must be upgraded together (documented in `docs/persistence.md`).
- **Length check before trimming:** the length is checked on the raw value, as it is for `Title`. A value of 100 characters or fewer that is padded with spaces to more than 100 characters is rejected.
- **No authentication:** `userId` is only what the client claims. `GET` still returns every user's tasks, and anyone can create tasks for any user.
- **OpenAPI request body test:** `RequestBody_IsDescribedByTheRequestParameter` fails if the `<param name="request">` text or position in `TodosController` changes while the remarks are edited.
- **Concurrency:** none. No version is introduced and every write still happens inside one `ExecuteAsync`.
