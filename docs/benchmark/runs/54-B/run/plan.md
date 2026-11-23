# Plan for #54: 0VP-T1-20 Benchmark: Optimistic concurrency for tasks

## Goal
Give every task a version number that the server manages. Clients must send back the version they last read on `PUT` (in the body) and `DELETE` (in the query). A stale write then gets `409 Conflict` ProblemDetails instead of overwriting newer data. The version check and the write happen in one atomic repository step, both in memory and in the shared JSON file.

## Acceptance criteria
1. Task responses expose `version` and old JSON files still load.
2. PUT with a matching version updates the task and increments its version. DELETE with a matching version removes it.
3. Stale PUT and DELETE requests return `409` ProblemDetails and preserve the current data. Missing or invalid versions return `400`.
4. An integration test with API instances sharing a JSON file proves a successful write invalidates a stale version on the other instance.
5. New tasks start at version 1. Tasks from older JSON files without `version` load as version 1. (derived)
6. A `PUT`/`DELETE` with a valid version for an unknown task still returns `404`. (derived)
7. The version check and the write are atomic in both storage modes: when several writes carry the same version, exactly one succeeds. (derived)
8. OpenAPI documents `version` on `TodoItemDto` and `UpdateTodoItemRequest`, the required `version` query parameter of `DELETE`, and the new `400`/`409` responses. (derived)
9. `POST /api/todos/{id}/complete` (no client version) also increments the version and returns the new value, and never overwrites a concurrent change. (derived)

## Decisions
- **Version type:** `long Version`, as `rules/concurrency.md` asks. On the entity it is declared `{ get; set; } = 1`. New tasks and JSON files without `version` therefore get 1 with no migration code. System.Text.Json keeps the initializer value when the property is missing from the file.
- **New repository methods instead of changing the old ones:** add `UpdateIfVersionMatchesAsync` and `DeleteIfVersionMatchesAsync`, both returning a new `TodoWriteResult` (`TodoWriteStatus` Success / NotFound / Conflict, plus the item). `UpdateAsync` and `DeleteAsync` keep their `bool` signatures. Reason: fewer changed public contracts, and no assertion in the existing repository tests has to change. `UpdateAsync` still increments the version inside the repository, so every successful write increments it.
- **Version in the PUT body:** `long?` with `[Required]` and `[Range(1, long.MaxValue)]`. This follows the existing `CreateRepeatingTodoItemRequest.FirstDueAtUtc` pattern: a missing value becomes `400` instead of quietly defaulting to `0`. No `required` keyword, for the reason given in the remarks on `CreateTodoItemRequest.Title`.
- **Version for DELETE:** `[FromQuery] long? version` with the same `[Required]` and `[Range]` attributes. `[ApiController]` then returns the standard `ValidationProblemDetails` automatically.
- **Where the check happens:** the controller only passes the client's version to the repository. The comparison and the increment happen inside the repository write: in `InMemoryTodoRepository` inside its `TryUpdate`/`TryRemove` loop, and in `JsonFileTodoRepository` inside one `ExecuteAsync`.
- **`POST /complete`:** the issue does not cover it, so it gets no version parameter and its documented responses stay `200 404 500`. It calls `UpdateIfVersionMatchesAsync(entity, entity.Version)` with the version it just read. On `Conflict` it reads the task again and retries. So it increments and returns the correct version and never overwrites a concurrent write. Returning `409` instead was rejected: it adds a response the issue did not ask for and would change the existing `Operation_DocumentsAllResponses` assertion for `/complete`.
- **409 response:** a private helper `ConflictTodo(Guid id, long currentVersion, long expectedVersion)` next to `NotFoundTodo`, built with `Problem(statusCode: 409, title:, detail:)`. The `detail` names the current version. It is declared as `[ProducesResponseType(StatusCodes.Status409Conflict)]`, the same way as 404, so ApiExplorer attaches `ProblemDetails`.
- **Commit order is Data → DTO → API** (the reverse of the usual DTO first). `TodoItemDto.Version` is `required`, so `TodoMapper.ToDto` must set it in the same commit, and that needs `TodoItem.Version` to exist already.

## Changes by layer
### DTO (`src/UAI.ZeroVP.DTO`)
- `src/UAI.ZeroVP.DTO/TodoItemDto.cs`: add `public required long Version { get; init; }` as the last property.
  - Czech XML `<summary>`: the version to send back in `PUT` (body) and `DELETE` (`?version=`). Add `<example>1</example>`.
  - **Caller that breaks the build, same commit:** in `src/UAI.ZeroVP.API/Mapping/TodoMapper.cs`, `ToDto` must set `Version = entity.Version` (otherwise error CS9035).
- `src/UAI.ZeroVP.DTO/UpdateTodoItemRequest.cs`: add `public long? Version { get; init; }`.
  - Attributes: `[Required(ErrorMessage = "Verze úkolu je povinná.")]` and `[Range(1, long.MaxValue, ErrorMessage = "Verze úkolu musí být kladné celé číslo.")]`.
  - Czech `<summary>` (the version the client last read), `<remarks>` (why it is `?`, as on `FirstDueAtUtc`), `<example>1</example>`.
  - Add one sentence about the version to the class summary.
  - **Callers that send PUT without a version, same commit so the suite stays green.** Adding the version is harmless before the API commit because the controller ignores it until then:
    - `tests/DTO/UpdateTodoItemRequestTests.cs`: every request initializer gets `Version = 1`.
    - `tests/API/TodosControllerTests.cs`: the 4 `Update` tests get `Version = item.Version`, or `1` for the unknown id.
    - `tests/API/TodoUserIdEndpointTests.cs`: the `Put_DoesNotChangeTheUser` body gets `"version":1`.
    - `tests/OpenApi/OpenApiDocumentTests.cs`: the PUT 404 row of `Response_MatchesDocument` and the PUT body in `SuccessfulTodoLifecycle_MatchesDocument` get `"version":1`.
    - `tests/Performance/ApiResponseTimeTests.cs`: `WriteScenarioAsync` sends `Version` taken from the created DTO.

### Data (`src/UAI.ZeroVP.Data`)
- `src/UAI.ZeroVP.Data/Entities/TodoItem.cs`: add `public long Version { get; set; } = 1;`. The Czech doc explains why the default is 1 (new tasks, and files saved before versions existed). `Clone()` copies `Version`.
- `src/UAI.ZeroVP.Data/Abstractions/TodoWriteStatus.cs` (new): `public enum TodoWriteStatus { Success, NotFound, Conflict }`, with Czech XML docs.
- `src/UAI.ZeroVP.Data/Abstractions/TodoWriteResult.cs` (new): `sealed record` with `required TodoWriteStatus Status` and `TodoItem? Item`.
  - What `Item` holds:
    - Success: the stored item after an update, or the removed item after a delete.
    - Conflict: the current stored item, so the API can name its version.
    - NotFound: `null`.
  - Static factories `Success(TodoItem)`, `Conflict(TodoItem)` and `NotFound()`.
  - Callers use property patterns such as `{ Status: Success, Item: { } item }`, so they need no `!`.
- `src/UAI.ZeroVP.Data/Abstractions/ITodoRepository.cs`:
  - Add `Task<TodoWriteResult> UpdateIfVersionMatchesAsync(TodoItem item, long expectedVersion, CancellationToken cancellationToken = default);`.
  - Add `Task<TodoWriteResult> DeleteIfVersionMatchesAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default);`.
  - Czech docs for both: why the comparison and the write are one step.
  - Update the `UpdateAsync` doc: it raises the version by 1 and checks nothing.
- `src/UAI.ZeroVP.Data/InMemory/InMemoryTodoRepository.cs`:
  - `UpdateIfVersionMatchesAsync`:
    - Guard with `ThrowIfNull(item)` and `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion)`, then check the cancellation token.
    - Loop on `_items.TryGetValue(item.Id, out var existing)`:
      - if `existing.Version != expectedVersion`, return `Conflict(existing.Clone())`;
      - otherwise `replacement = item.Clone()` with `replacement.Version = existing.Version + 1`, and if `TryUpdate(item.Id, replacement, existing)` succeeds, return `Success(replacement.Clone())`.
    - When the loop ends, return `NotFound()`.
  - `DeleteIfVersionMatchesAsync`: the same loop. On a version match, use `_items.TryRemove(KeyValuePair.Create(id, existing))`, which removes only that exact instance. Return `Success(existing.Clone())`, `Conflict(existing.Clone())` or `NotFound()`.
  - `UpdateAsync`: inside the existing `TryUpdate` loop, set `replacement.Version = existing.Version + 1` before each `TryUpdate`. The signature and the `bool` return stay the same.
  - `AddAsync`/`AddRangeAsync`: no change (the entity default of 1 applies).
- `src/UAI.ZeroVP.Data/JsonFile/JsonFileTodoRepository.cs`:
  - `UpdateIfVersionMatchesAsync`: guard `item` and `expectedVersion` before `ExecuteAsync`, then `ExecuteAsync(repository => repository.UpdateIfVersionMatchesAsync(item, expectedVersion, cancellationToken), isWrite: true, cancellationToken)`.
  - `DeleteIfVersionMatchesAsync`: the same pattern.
  - Each operation is one `ExecuteAsync`, so the check and the write both run under the existing process lock and file lock.
  - `UpdateAsync` needs no code change because it delegates to `InMemoryTodoRepository`.
- `src/UAI.ZeroVP.Data/JsonFile/JsonFileTodoStore.cs`: no change. `version` is serialized as camelCase, and a missing property keeps the initializer value 1.
- **Interface implementations to update in the same commit, or the test project does not build:**
  - `tests/API/TodosControllerTests.cs`, `DeletedBeforeUpdateRepository`: `UpdateIfVersionMatchesAsync` first calls `inner.DeleteAsync(item.Id, ...)` and then delegates. This keeps `Update_ItemDeletedMeanwhile_ReturnsNotFound` and `Complete_ItemDeletedMeanwhile_ReturnsNotFound` meaningful once the controller switches methods. `DeleteIfVersionMatchesAsync` just delegates.
  - `tests/OpenApi/OpenApiDocumentTests.cs`, `FailingTodoRepository`: both new methods `=> throw Failure();`.

### API (`src/UAI.ZeroVP.API`)
- `src/UAI.ZeroVP.API/Mapping/TodoMapper.cs`:
  - `ToDto` gets `Version = entity.Version`. This goes in the DTO commit, see above.
  - `ToEntity` relies on the entity default of 1.
  - `ApplyTo` must **not** touch `Version`, because only the repository changes it.
- `src/UAI.ZeroVP.API/Controllers/TodosController.cs`:
  - **`Update`:**
    - `var expectedVersion = request.Version ?? throw new ArgumentException(...)`. This cannot happen after `[ApiController]` validation, but it means a missing version can never skip the check.
    - Keep `GetByIdAsync` → `NotFoundTodo` and `ApplyTo`.
    - Then call `UpdateIfVersionMatchesAsync(entity, expectedVersion, cancellationToken)` and map the result: Success → `Ok(item.ToDto())`, Conflict → `ConflictTodo(id, current.Version, expectedVersion)`, NotFound → `NotFoundTodo(id)`.
    - Add `[ProducesResponseType(StatusCodes.Status409Conflict)]` and `<response code="409">`. Mention `version` in the `<remarks>` and the 400 text.
  - **`Complete`:** loop as follows:
    - `GetByIdAsync`; if `null`, return `NotFoundTodo`.
    - `SetCompletionState`, then `UpdateIfVersionMatchesAsync(entity, entity.Version, ...)`.
    - Success → `Ok(item.ToDto())`; NotFound → 404; Conflict → read again.
    - Add a Czech comment explaining why it retries. Responses and attributes stay unchanged.
  - **`Delete`:**
    - New signature: `Delete(Guid id, [FromQuery, Required(...), Range(1, long.MaxValue, ...)] long? version, CancellationToken cancellationToken)`, using the same Czech messages as the DTO.
    - Call `DeleteIfVersionMatchesAsync`. Success → the existing log line + `NoContent()`; Conflict → `ConflictTodo`; NotFound → `NotFoundTodo`.
    - Add `[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]` and `[ProducesResponseType(StatusCodes.Status409Conflict)]`.
    - Add XML `<param name="version">`, `<response code="400">` and `<response code="409">`.
  - Add a private `ConflictTodo(...)` with a Czech `title` (e.g. "Konflikt verzí úkolu") and a `detail` that names the id, the current version and the version that was sent.
  - Add `using System.ComponentModel.DataAnnotations;`.
  - **Callers of the new `Delete` signature, same commit:** `tests/API/TodosControllerTests.cs` lines 280, 392 and 403 pass `version:` (the item's version, or `1` for the unknown id).
  - **HTTP DELETE callers, same commit:**
    - `tests/OpenApi/OpenApiDocumentTests.cs`: the DELETE 404 row URL gets `?version=1`; the lifecycle DELETE sends the version read from the `/complete` response.
    - `tests/Performance/ApiResponseTimeTests.cs`: the DELETE uses the version read from the `/complete` response.
- `src/UAI.ZeroVP.API/Mcp/TodoTools.cs`, `Program.cs` and `OpenApi/*`: no change. MCP output gets `version` automatically through `ToDto`.

## Tests (`tests/`)
- `tests/Data/TodoItemTests.cs`:
  - Set `Version = 4` in the `CreateFullyPopulatedItem` helper so that `Clone_CopiesEveryPublicProperty` actually covers it (arrange-only change).
  - `NewItem_WithoutVersion_StartsAtVersionOne` → AC 5
- `tests/Data/JsonFileTodoStoreTests.cs`:
  - `Load_FileFromBeforeVersions_LoadsTasksWithVersionOne` (a file with no `version`, modelled on `Load_FileFromBeforeUsers_LoadsTasksWithoutUser`) → AC 1, 5
  - Set `Version = 3` in `SaveThenLoad_ReturnsAllFields` (arrange only) → AC 1
  - `Save_WritesVersion` → AC 1
- `tests/Data/InMemoryTodoRepositoryTests.cs`:
  - `UpdateIfVersionMatchesAsync_MatchingVersion_ReturnsSuccessAndIncrementsVersion` → AC 2
  - `UpdateIfVersionMatchesAsync_StaleVersion_ReturnsConflictAndKeepsStoredItem` → AC 3
  - `UpdateIfVersionMatchesAsync_SameVersionTwice_FirstWinsSecondConflicts` → AC 3, 7
  - `UpdateIfVersionMatchesAsync_ConcurrentUpdatesWithSameVersion_ExactlyOneSucceeds` (1000 × `Task.Run`, exactly one Success, final version 2) → AC 7
  - `UpdateIfVersionMatchesAsync_UnknownItem_ReturnsNotFoundAndDoesNotAddIt` → AC 6
  - `UpdateIfVersionMatchesAsync_NonPositiveVersion_ThrowsArgumentOutOfRange` → AC 3
  - `DeleteIfVersionMatchesAsync_MatchingVersion_ReturnsSuccessAndRemovesItem` → AC 2
  - `DeleteIfVersionMatchesAsync_StaleVersion_ReturnsConflictAndKeepsItem` → AC 3
  - `DeleteIfVersionMatchesAsync_UnknownId_ReturnsNotFound` → AC 6
  - `UpdateAsync_ExistingItem_IncrementsVersion` → AC 9
  - `AddAsync_NewItem_StartsAtVersionOne` → AC 5
  - Add rows `"UpdateIfVersionMatches"` and `"DeleteIfVersionMatches"` to `AllOperations` (used by `AnyOperation_CancelledToken_ThrowsOperationCanceledException`) → AC 2
- `tests/Data/JsonFileTodoRepositoryTests.cs`:
  - `UpdateIfVersionMatches_ThroughOneInstance_MakesVersionStaleOnAnother` → AC 2, 3, 4
  - `DeleteIfVersionMatches_StaleVersion_KeepsTaskInFile` → AC 3
  - `DeleteIfVersionMatches_MatchingVersion_RemovesTaskFromFile` → AC 2
  - `UpdateIfVersionMatches_UnknownItem_ReturnsNotFound` → AC 6
  - `ConcurrentUpdatesWithSameVersion_FromTwoInstances_ExactlyOneSucceeds` → AC 7
- `tests/DTO/UpdateTodoItemRequestTests.cs` (existing initializers get `Version = 1`, see DTO section):
  - `Validate_MissingVersion_FailsOnVersion` → AC 3
  - `Validate_NonPositiveVersion_FailsOnVersion` (Theory: 0, -1, `long.MinValue`) → AC 3
  - `Validate_PositiveVersion_IsValid` (Theory: 1, `long.MaxValue`) → AC 2
- `tests/API/TodoMapperTests.cs`:
  - Set `Version = 7` in the `CreateEntity` helper so that `ToDto_MapsEveryDtoPropertyFromSameNamedEntityProperty` covers it (arrange only).
  - `ToDto_CopiesVersion` → AC 1
  - `ToEntity_NewItemStartsAtVersionOne` → AC 5
  - `ApplyTo_DoesNotChangeVersion` → AC 2
- `tests/API/TodosControllerTests.cs` (fake and call-site updates listed above; add an `AssertConflictProblem(result, currentVersion)` helper):
  - `Create_ReturnsItemWithVersionOne` → AC 1, 5
  - `Update_MatchingVersion_ReturnsOkWithIncrementedVersion` → AC 2
  - `Update_StaleVersion_ReturnsConflictProblemAndKeepsStoredItem` → AC 3
  - `Delete_StaleVersion_ReturnsConflictProblemAndKeepsItem` → AC 3
  - `Complete_OpenItem_ReturnsIncrementedVersion` → AC 9
  - `Complete_ItemChangedMeanwhile_CompletesLatestStateWithNextVersion`. It uses a new private fake that makes one unconditional `UpdateAsync` before the first conditional call. Asserts: the changed title is kept, the task is completed, `Version == 3`. → AC 9
- `tests/API/TodoVersionEndpointTests.cs` (new, `IClassFixture<ApiTestFactory>`, namespace `UAI.ZeroVP.Tests.API`):
  - `Post_NewTask_ReturnsVersionOneInEveryResponse` (POST, GET by id, list) → AC 1, 5
  - `Put_MatchingVersion_Returns200AndIncrementsVersion` → AC 2
  - `Put_StaleVersion_Returns409ProblemAndKeepsTask` (problem `status` 409, `detail` contains the current version, GET shows the original title and version) → AC 3
  - `Put_SameVersionTwice_FirstWinsSecondReturns409` → AC 3, 7
  - `Put_MissingOrInvalidVersion_Returns400AndKeepsTask`. Cases and expected `errors` key:
    - missing, `null`, `0` or `-1` → `Version`;
    - `"1"` (a string; JSON numbers are strict) or `1.5` → `$.version`.
    - → AC 3
  - `Put_UnknownIdWithValidVersion_Returns404` → AC 6
  - `Delete_MatchingVersion_Returns204AndRemovesTask` → AC 2
  - `Delete_StaleVersion_Returns409ProblemAndKeepsTask` → AC 3
  - `Delete_MissingOrInvalidVersion_Returns400AndKeepsTask` (no query, `?version=`, `?version=0`, `?version=-1`, `?version=abc`; `errors` key `version`) → AC 3
  - `Delete_UnknownIdWithValidVersion_Returns404` → AC 6
  - `Complete_IncrementsVersion` → AC 9
- `tests/API/PersistentApiIntegrationTests.cs` (`PersistentApiTestFactory`, two hosts on one file):
  - `TwoHostsUsingSameFile_UpdateThroughOne_MakesVersionStaleOnOther`. Steps:
    1. A: POST → v1; B: GET → v1.
    2. A: PUT v1 → 200 v2.
    3. B: PUT v1 → 409, and `detail` names 2.
    4. B: GET → A's title, v2.
    5. B: PUT v2 → 200 v3.
    - → AC 4, 2, 3
  - `TwoHostsUsingSameFile_DeleteWithStaleVersion_Returns409AndKeepsTask` → AC 3, 4
  - `FileFromBeforeVersions_LoadsWithVersionOneAndAcceptsUpdate`. Write a file with no `version`; GET → 1; PUT `version: 1` → 200 with 2; the file now contains `"version": 2`. → AC 1, 5
- `tests/OpenApi/OpenApiDocumentTests.cs` (fake and input updates listed above):
  - `Operation_DocumentsAllResponses`: change the PUT row to `"200 400 404 409 415 500"` and the DELETE row to `"204 400 404 409 500"` → AC 8. This changes an existing assertion, which the issue explicitly requires; say so in the commit message and the PR.
  - `Response_MatchesDocument`: add rows for PUT without version → 400, DELETE without version → 400, and DELETE `?version=abc` → 400 → AC 3, 8. The existing 404 rows now send a valid version → AC 6.
  - `SuccessfulTodoLifecycle_MatchesDocument` (sends versions as above) → AC 2, 8
  - `StaleVersion_ConflictResponses_MatchDocument`: real 409 responses from PUT and DELETE are documented, including content type and `X-Instance-Id` → AC 3, 8
  - `TodoItemDto_DocumentsVersionAsRequiredInteger` → AC 1, 8
  - `UpdateRequest_DocumentsVersionAsRequired` → AC 8
  - `Delete_DocumentsRequiredVersionQueryParameter` (`In == Query`, `Required`, integer schema) → AC 8
- `tests/Performance/ApiResponseTimeTests.cs`: change the inputs only, as listed above, so the skipped write scenario stays valid. No new test.

## Documentation
- `README.md`:
  - Endpoint table: the PUT row mentions `version` in the body and `409`; the DELETE row mentions the required `?version=` and `409`.
  - New subsection "Verze úkolu" covering: `version` in every response, starting at 1 (also for old files), `409` for a stale version, `400` for a missing or invalid one, and `/complete` incrementing the version.
  - Write it in Czech to match the surrounding README text. `rules/csharp-style.md` says README must be English, but per `rules/README.md` the existing code and docs win; mention this mismatch in the PR.
- `docs/openapi.md`:
  - Summary error list: add `409`.
  - "Documented endpoints": the PUT row gets `version` in `UpdateTodoItemRequest` and `409`; the DELETE row gets query `version` (integer, required), `400` and `409`.
  - Tests table: add the new OpenAPI tests.
- `docs/persistence.md`:
  - File-format example: add `"version": 1`.
  - Add a note that files saved before versions load with version 1.
  - Add a short "Optimistic concurrency" paragraph: the check and the write run inside one locked read–change–write cycle.
  - Tests table: add the new store and repository tests.
- `docs/multiple-instances.md`: add a section saying that a write through one instance makes older versions stale on all instances (`409`). Add `PersistentApiIntegrationTests` to the tests table.
- `docs/mcp.md`: add `"version": 1` to the `get_todo` example output.

## Risks
- **Breaking change for API clients (intended by the issue):** `PUT` and `DELETE` without a version now return `400`. Every in-repo caller is listed above.
- **File compatibility:** the next write adds `version` to `todos.json`. Older builds still read the file, because System.Text.Json ignores unknown properties.
- **Hand-edited versions:** a value of `0` or below loads as-is, and such a task cannot be updated or deleted through the API (that request gets `400`). This is not handled because the issue does not ask for it.
- **Unnecessary file rewrite:** the new JSON-file methods use `isWrite: true`, so a `Conflict` or `NotFound` result also rewrites the file with identical content. The data does not change. The existing `UpdateAsync` already does the same when the task is not found.
- **`/complete` retry loop:** it has no fixed limit. Each retry means another write succeeded, so the loop cannot spin forever across requests, and the request's `CancellationToken` ends it. Only a single task under heavy contention could make one request wait longer.
- **Tests failing between commits:** the DTO commit makes `[Required] Version` effective. All PUT callers listed in the DTO section must get a version in that same commit, or the suite fails in between commits.
- **Existing tests:** only the `Operation_DocumentsAllResponses` rows for PUT and DELETE change an assertion. The other edits to existing tests only add a version to their inputs or fill test helpers. Say this in the commit messages and the PR.
