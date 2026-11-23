## Summary
Every task now has a `version` that the server manages. It starts at 1 and goes up by exactly 1 on every successful write. `PUT /api/todos/{id}` must send the version the client last read in the JSON body, and `DELETE /api/todos/{id}` must send it as `?version=`. A stale version returns `409 Conflict` as ProblemDetails, with a `detail` that names the current version, and nothing is written. A missing or invalid version returns the standard `400` validation response, and a valid version for an unknown task still returns `404`. The version check and the write are one atomic repository step, both in memory and under the existing shared-file lock. A successful write through one API instance therefore makes the old version stale on every instance.

**Breaking change (the issue requires it):** `PUT` and `DELETE` requests without a version now get `400`. Every caller in the repository (tests and the performance scenario) was updated.

## Changes
- **DTO:**
  - `TodoItemDto.Version`: `long`, required, serialized as `version`.
  - `UpdateTodoItemRequest.Version`: `long?` with `[Required]` and `[Range(1, long.MaxValue)]`, so a missing value is a `400` instead of quietly becoming `0`.
- **Data:**
  - `TodoItem.Version` defaults to `1`. New tasks, and tasks from JSON files saved before versions existed, therefore start at 1 with no migration. `Clone()` copies the version.
  - New `ITodoRepository.UpdateIfVersionMatchesAsync` and `DeleteIfVersionMatchesAsync` return a `TodoWriteResult`: a `TodoWriteStatus` (`Success` / `NotFound` / `Conflict`) plus the item. A conflict is a result, not an exception.
  - `InMemoryTodoRepository` compares the version inside its `TryUpdate` loop (and a key/value `TryRemove` for delete). A concurrent swap makes the exchange fail, and the next pass sees the new version, so a concurrent write is never overwritten.
  - `JsonFileTodoRepository` runs each check-and-write in one `ExecuteAsync` call, so both steps run under the process lock and the file lock.
  - `UpdateAsync` and `DeleteAsync` keep their `bool` signatures. `UpdateAsync` now also raises the version.
- **API:**
  - `PUT /api/todos/{id}` passes `version` to `UpdateIfVersionMatchesAsync`. The results map to `200` (task with the new version), `409` or `404`.
  - `DELETE /api/todos/{id}` takes a required `[FromQuery] long? version` with the same validation messages. The results map to `204`, `409` or `404`.
  - The `409` comes from a new `ConflictTodo` helper (title "Konflikt verzí úkolu"; `detail` names the current version and the version that was sent). Like the other controller errors it is sent as `application/json`, as `docs/openapi.md` documents.
  - `POST /api/todos/{id}/complete` takes no version from the client. It writes on condition of the version it just read, and on a conflict it reads the task again. So it raises the version, returns the new value and never overwrites a concurrent change. Its documented responses stay `200 404 500`.
  - OpenAPI picks up the new property, the required query parameter and the `400`/`409` responses from attributes and XML docs. No transformer changed. MCP `get_todo` / `list_todos` output includes `version` through `ToDto`.
- **Tests:**
  - New class `TodoVersionEndpointTests`.
  - New tests in `PersistentApiIntegrationTests`, `OpenApiDocumentTests`, `TodosControllerTests`, `TodoMapperTests`, `UpdateTodoItemRequestTests`, `InMemoryTodoRepositoryTests`, `JsonFileTodoRepositoryTests`, `JsonFileTodoStoreTests` and `TodoItemTests`.
  - The mapping to the acceptance criteria is below, and changes to existing tests are listed separately.
- **Docs:**
  - `README.md`: the PUT and DELETE rows of the endpoint table, plus a new "Verze úkolu" section.
  - `docs/openapi.md`: the `409` response, `version` in the PUT body and as a DELETE query parameter, and the new tests.
  - `docs/persistence.md`: `version` in the file format, old files load as version 1, a new "Optimistic concurrency" section, and the tests table.
  - `docs/multiple-instances.md`: a new section on stale versions across instances, with a curl walk-through, and the tests table.
  - `docs/mcp.md`: `version` in the `get_todo` example.
  - See the README language note below.

## Verification
| Check | Result |
|---|---|
| `dotnet test --configuration Release` | 393/395 passed, 0 failed, 2 skipped |
| First attempt passed | no |
| Repair cycles | 1 |
| `git diff --check` | clean |

- The 2 skipped tests are the existing performance tests.
- The tests ran on `2a614bd`. The three `docs(...)` commits after it change only Markdown.
- `git diff --check` was run on the final branch.
- **Repair cycle:** attempt 1 had 17 failures, all on one assertion. The new tests expected `application/problem+json` for `400`/`404`/`409`. `TodosController` has `[Produces("application/json")]`, so controller errors are sent as `application/json`, which is the documented base-branch behaviour (`docs/openapi.md`). Repair `2a614bd` changed the expected content type only in tests added on this branch (`TodoVersionEndpointTests`, `PersistentApiIntegrationTests`). No assertion was removed, and every status, `detail` and `errors`-key check is unchanged.

### Acceptance criteria
| Criterion | Covered by |
|---|---|
| Task responses expose `version` and old JSON files still load | `TodoVersionEndpointTests.Post_NewTask_ReturnsVersionOneInEveryResponse`, `TodoMapperTests.ToDto_CopiesVersion`, `JsonFileTodoStoreTests.Load_FileFromBeforeVersions_LoadsTasksWithVersionOne`, `JsonFileTodoStoreTests.Save_WritesVersion`, `PersistentApiIntegrationTests.FileFromBeforeVersions_LoadsWithVersionOneAndAcceptsUpdate`, `OpenApiDocumentTests.TodoItemDto_DocumentsVersionAsRequiredInteger` |
| PUT with a matching version updates the task and increments its version; DELETE with a matching version removes it | `TodoVersionEndpointTests.Put_MatchingVersion_Returns200AndIncrementsVersion`, `TodoVersionEndpointTests.Delete_MatchingVersion_Returns204AndRemovesTask`, `TodosControllerTests.Update_MatchingVersion_ReturnsOkWithIncrementedVersion`, `InMemoryTodoRepositoryTests.UpdateIfVersionMatchesAsync_MatchingVersion_ReturnsSuccessAndIncrementsVersion`, `InMemoryTodoRepositoryTests.DeleteIfVersionMatchesAsync_MatchingVersion_ReturnsSuccessAndRemovesItem`, `JsonFileTodoRepositoryTests.DeleteIfVersionMatches_MatchingVersion_RemovesTaskFromFile`, `TodoMapperTests.ApplyTo_DoesNotChangeVersion` |
| Stale PUT and DELETE return `409` ProblemDetails and preserve the current data; missing or invalid versions return `400` | `TodoVersionEndpointTests.Put_StaleVersion_Returns409ProblemAndKeepsTask`, `TodoVersionEndpointTests.Delete_StaleVersion_Returns409ProblemAndKeepsTask`, `TodoVersionEndpointTests.Put_SameVersionTwice_FirstWinsSecondReturns409`, `TodoVersionEndpointTests.Put_MissingOrInvalidVersion_Returns400AndKeepsTask` (missing, `null`, `0`, `-1`, `"1"`, `1.5`), `TodoVersionEndpointTests.Delete_MissingOrInvalidVersion_Returns400AndKeepsTask` (no query, `?version=`, `0`, `-1`, `abc`), `TodosControllerTests.Update_StaleVersion_ReturnsConflictProblemAndKeepsStoredItem`, `TodosControllerTests.Delete_StaleVersion_ReturnsConflictProblemAndKeepsItem`, `UpdateTodoItemRequestTests.Validate_MissingVersion_FailsOnVersion`, `UpdateTodoItemRequestTests.Validate_NonPositiveVersion_FailsOnVersion`, `InMemoryTodoRepositoryTests.UpdateIfVersionMatchesAsync_StaleVersion_ReturnsConflictAndKeepsStoredItem`, `InMemoryTodoRepositoryTests.DeleteIfVersionMatchesAsync_StaleVersion_ReturnsConflictAndKeepsItem`, `JsonFileTodoRepositoryTests.DeleteIfVersionMatches_StaleVersion_KeepsTaskInFile`, `OpenApiDocumentTests.StaleVersion_ConflictResponses_MatchDocument` |
| An integration test with API instances sharing a JSON file proves a successful write invalidates a stale version on the other instance | `PersistentApiIntegrationTests.TwoHostsUsingSameFile_UpdateThroughOne_MakesVersionStaleOnOther` (A writes v1 → v2; B's v1 gets `409` naming 2; B reads A's change and v2; B writes v2 → v3), `PersistentApiIntegrationTests.TwoHostsUsingSameFile_DeleteWithStaleVersion_Returns409AndKeepsTask`, plus `JsonFileTodoRepositoryTests.UpdateIfVersionMatches_ThroughOneInstance_MakesVersionStaleOnAnother` at the repository level |
| *(derived)* New tasks start at version 1 | `TodoItemTests.NewItem_WithoutVersion_StartsAtVersionOne`, `InMemoryTodoRepositoryTests.AddAsync_NewItem_StartsAtVersionOne`, `TodoMapperTests.ToEntity_NewItemStartsAtVersionOne`, `TodosControllerTests.Create_ReturnsItemWithVersionOne` |
| *(derived)* A valid version for an unknown task still returns `404` | `TodoVersionEndpointTests.Put_UnknownIdWithValidVersion_Returns404`, `TodoVersionEndpointTests.Delete_UnknownIdWithValidVersion_Returns404`, `InMemoryTodoRepositoryTests.UpdateIfVersionMatchesAsync_UnknownItem_ReturnsNotFoundAndDoesNotAddIt`, `InMemoryTodoRepositoryTests.DeleteIfVersionMatchesAsync_UnknownId_ReturnsNotFound`, `JsonFileTodoRepositoryTests.UpdateIfVersionMatches_UnknownItem_ReturnsNotFound`, the 404 rows of `OpenApiDocumentTests.Response_MatchesDocument` |
| *(derived)* Check and write are atomic in both storage modes | `InMemoryTodoRepositoryTests.UpdateIfVersionMatchesAsync_ConcurrentUpdatesWithSameVersion_ExactlyOneSucceeds`, `InMemoryTodoRepositoryTests.UpdateIfVersionMatchesAsync_SameVersionTwice_FirstWinsSecondConflicts`, `JsonFileTodoRepositoryTests.ConcurrentUpdatesWithSameVersion_FromTwoInstances_ExactlyOneSucceeds` |
| *(derived)* OpenAPI documents `version`, the required DELETE query parameter and `400`/`409` | `OpenApiDocumentTests.TodoItemDto_DocumentsVersionAsRequiredInteger`, `OpenApiDocumentTests.UpdateRequest_DocumentsVersionAsRequired`, `OpenApiDocumentTests.Delete_DocumentsRequiredVersionQueryParameter`, `OpenApiDocumentTests.Operation_DocumentsAllResponses`, `OpenApiDocumentTests.Response_MatchesDocument`, `OpenApiDocumentTests.SuccessfulTodoLifecycle_MatchesDocument` |
| *(derived)* `POST /complete` increments the version and never overwrites a concurrent change | `TodoVersionEndpointTests.Complete_IncrementsVersion`, `TodosControllerTests.Complete_OpenItem_ReturnsIncrementedVersion`, `TodosControllerTests.Complete_ItemChangedMeanwhile_CompletesLatestStateWithNextVersion`, `InMemoryTodoRepositoryTests.UpdateAsync_ExistingItem_IncrementsVersion` |

### Changed existing tests
- **Changed assertion:** in `OpenApiDocumentTests.Operation_DocumentsAllResponses`, the PUT row changes from `200 400 404 415 500` to `200 400 404 409 415 500`, and the DELETE row from `204 404 500` to `204 400 404 409 500`. The issue requires this, because the new `400` and `409` responses must be documented. These two rows are the only changed assertions in existing tests. They are in the `feat(api)` commit, and its message says an existing assertion changes.
- **Input-only changes (no assertion changed):** existing PUT and DELETE calls now send a version:
  - the `UpdateTodoItemRequestTests` initializers;
  - the `TodosControllerTests` `Update` tests and `Delete` call sites;
  - `TodoUserIdEndpointTests.Put_DoesNotChangeTheUser`;
  - the PUT and DELETE `404` rows and the lifecycle in `OpenApiDocumentTests`;
  - the `ApiResponseTimeTests` write scenario.
- **Helpers and fakes:** test helpers now fill `Version`, so the existing "every property" tests cover it. These are `TodoItemTests`, `TodoMapperTests` and `JsonFileTodoStoreTests.SaveThenLoad_ReturnsAllFields`. The fakes `DeletedBeforeUpdateRepository` and `FailingTodoRepository` implement the two new interface methods.

### Deviations from the plan
- **Commit split:** the API layer is three commits (`feat(api)`, `test(api)`, `test(openapi)`). The changed PUT and DELETE rows of `Operation_DocumentsAllResponses` are in `feat(api)`, so the suite never goes red between commits. The new DTO tests are in `feat(dto)` because they share `UpdateTodoItemRequestTests.cs` with the caller fixes. The `TodoMapperTests` additions are in `test(api)`.
- **Extra fix-up commit:** `ce50d9b` removes a null-forgiving `Load()!` from `Load_FileFromBeforeVersions_LoadsTasksWithVersionOne`, because amending is not allowed.
- **Formatter side effects:** `dotnet format whitespace --include` also fixed whitespace that already existed in touched files. It re-indented four `GetAllAsync_User*` tests in `InMemoryTodoRepositoryTests`, fixed one misindented doc line and trailing spaces in `InMemoryTodoRepository` and `AssertWithinBudget` in `ApiResponseTimeTests`, and added missing final newlines. The changes are whitespace only, and the commit messages say so.
- **`TodoWriteResult` guards:** the `Success` and `Conflict` factories guard their argument with `ThrowIfNull`, as `rules/csharp-style.md` asks. The plan did not mention this.
- **`OpenApiDocumentTests` helpers:** the lifecycle PUT uses its own `updateBody` with `"version":1` instead of reusing the POST body. Small `ReadVersionAsync` and `GetSchema` helpers were added.
- **`ApiResponseTimeTests`:** the existing `!.Id` was replaced with `Assert.NotNull` while adding the version reads.
- **`DeletedBeforeUpdateRepository`:** its doc comment in `TodosControllerTests` now mentions `UpdateIfVersionMatchesAsync`.
- **Docs beyond the plan's list:**
  - `docs/openapi.md` gets a short paragraph on `version` and `409`.
  - `docs/persistence.md` explains that a conflict still rewrites the file with identical content, and lists `PersistentApiIntegrationTests`.
  - `docs/multiple-instances.md` gets a curl walk-through.

### Notes for the reviewer
- **README language mismatch:** `rules/csharp-style.md` and `rules/repository.md` say `README.md` and `docs/` are written in English, but `README.md` (and `docs/mcp.md`) are in Czech. Per `rules/README.md`, the existing code and docs win over the rules, so the new README section "Verze úkolu" is in Czech. The `docs/mcp.md` change is only a JSON example. The other docs files were already in English and stay English.
- **`/complete` has no client version:** `rules/concurrency.md` gives a `version` query parameter for `POST /complete` as an example. The issue does not ask for one, so `/complete` takes no version and retries on a conflict instead, and its documented responses are unchanged. A client-supplied version for `/complete` would be a follow-up.
- **File rewrite on conflict:** the version-checked JSON-file writes use `isWrite: true`, so a `Conflict` or `NotFound` result still rewrites `todos.json` with identical content. The existing `UpdateAsync` already does the same for an unknown id.
- **File compatibility:** the next write adds `version` to `todos.json`. Older builds can still read the file, because the serializer ignores unknown properties.
- **Not handled (out of the issue's scope):** a hand-edited `version` of `0` or below loads as-is. Such a task cannot be updated or deleted through the API, because the request gets `400`.

🤖 Generated by `/issue-to-pr` with [Claude Code](https://claude.com/claude-code)

Closes #54
