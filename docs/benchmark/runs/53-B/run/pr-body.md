## Summary
Every task now has an owner, `userId`: a plain string such as a name, ID or e-mail, with no authentication or access control. `POST /api/todos` and `POST /api/todos/repeat` require it, store it trimmed and return it, and every `GET` returns it. The owner is fixed at creation: `PUT` ignores a `userId` in its body. Tasks from older JSON files without `userId` still load and are returned with `"userId": null`. **Breaking change for clients:** a `POST` without a valid `userId` now gets `400`, as the issue asks.

## Changes
- **DTO:** `CreateTodoItemRequest` and `CreateRepeatingTodoItemRequest` get a required `userId` of 1–100 characters. A missing, `null`, empty or whitespace-only value fails validation. The length is checked before trimming, as it is for `title`. `TodoItemDto` always contains `userId`, which is `null` for tasks saved before owners existed. `UpdateTodoItemRequest` does not change, so the owner cannot be changed.
- **Data:** `TodoItem.UserId` is a nullable `init` property and is not `required`, so files without `userId` are not treated as corrupted. `Clone()` copies it. The repositories and `JsonFileTodoStore` do not change, and the field is saved to the JSON file automatically.
- **API:**
  - `POST /api/todos` stores the trimmed `userId`, keeps its letter case, and returns it in the `201` response.
  - `POST /api/todos/repeat` gives every occurrence the same `userId`.
  - An invalid `userId` returns `400` `ValidationProblemDetails` and creates no task.
  - `GET /api/todos` and `GET /api/todos/{id}` return `userId`.
  - `PUT /api/todos/{id}` and `POST /api/todos/{id}/complete` keep the original owner. A `userId` in a `PUT` body is ignored.
  - The controller XML docs and the `.http` samples are updated. They include a new sample for an invalid `userId`.
- **Tests:**
  - New `TodoUserIdEndpointTests` (over HTTP). They check that:
    - `POST /api/todos` returns the trimmed `userId`, and every `/repeat` occurrence carries it;
    - on both `POST` URLs, a missing, `null`, `""`, `"   "`, 101-character or numeric `userId` returns `400` and leaves the task count unchanged;
    - a 100-character `userId` is accepted;
    - `GET` by id and the `GET` list return `userId`;
    - `PUT` with a different `userId` keeps the owner;
    - a task from a file without `userId` is returned with `"userId": null`.
  - DTO validation, in both create request test classes: `Validate_MissingUserId_FailsOnUserId`, `Validate_UserIdWithinLimit_IsValid` and `Validate_UserIdOverLimit_FailsOnUserId`.
  - Mapping (`TodoMapperTests`): the value is copied to the DTO, `null` stays `null`, the value is trimmed and keeps its case, every occurrence gets it, and `ApplyTo` keeps the owner.
  - Controller (`TodosControllerTests`): `Create`, `CreateRepeating`, `GetAll`, `GetById`, `Update` and `Complete` return or keep `userId`.
  - Repository:
    - `InMemoryTodoRepositoryTests`: add, batch add, update, and initial items without `userId`.
    - `JsonFileTodoStoreTests`: `userId` is written, and a file without `userId` or with `"userId": null` loads with a `null` owner and is not moved aside.
    - `JsonFileTodoRepositoryTests`: `userId` is visible through a second instance, and tasks from an older file survive a write.
  - OpenAPI: `UserId_IsDocumentedAsRequiredWithLengthLimits`, `TodoItemDto_DocumentsUserIdAsRequired` and `UpdateRequest_DoesNotDocumentUserId`.
  - **Changed existing tests (request and fixture data only, no assertion changed).** Because `userId` is now required, it was added to:
    - the request data in `CreateTodoItemRequestTests` and `CreateRepeatingTodoItemRequestTests`;
    - in `RepeatingTodosEndpointTests`: the valid body, every invalid-request case (each still fails only on its own field) and `PostTodo_WithoutDueDate_ReturnsDueAtUtcNull`;
    - `RepeatBody` and the lifecycle body in `OpenApiDocumentTests`;
    - `ApiResponseTimeTests`, which is skipped by default;
    - the fixtures in `TodoItemTests`, `JsonFileTodoStoreTests.SaveThenLoad_ReturnsAllFields` and the `TodoMapperTests`/`TodosControllerTests` helpers.

    `Validate_TitleOnly_IsValid` was renamed to `Validate_RequiredFieldsOnly_IsValid`, because a title alone is no longer valid.
- **Docs:**
  - `README.md` (Czech, like the rest of the file):
    - the endpoint table;
    - a new section about `userId`;
    - the `/repeat` fields;
    - the `curl` examples.
  - `docs/openapi.md`: `userId` in the schemas, and the three new tests.
  - `docs/persistence.md`:
    - `userId` in the file format;
    - older files load with a `null` owner;
    - all instances that share a file must be upgraded together.
  - `docs/multiple-instances.md`: `userId` in the example `POST` body.

## Verification
| Check | Result |
|---|---|
| `dotnet test --configuration Release` | 345/347 passed, 0 failed, 2 skipped |
| First attempt passed | yes |
| Repair cycles | 0 |
| `git diff --check` | clean |

### Deviations from the plan
- The plan's 4th commit, `docs(api)`, was made by the release stage and not the implementer, because the implementer may change only `src/` and `tests/`. The tests ran before this commit, which changes only Markdown files.
- In both create request test classes, `Validate_MissingUserId_FailsOnUserId` builds the request by deserializing JSON (web defaults) instead of using an object initializer. This avoids a `!` when assigning `null` to the non-nullable `UserId`. It covers `null`, `""`, `"   "` and a missing field.
- `JsonFileTodoRepositoryTests`: the `CreateItem` helper got an optional `userId` parameter. The plan mentions this only for the in-memory and controller tests. `LegacyFileWithoutUserId_TasksSurviveAWrite` also checks that the file contains `"userId": null` after the write.
- `OpenApiDocumentTests`: a small `LoadSchemaAsync` helper was added, so the new tests need no `!`.
- Whitespace-only fixes in touched files:
  - trailing whitespace in `OpenApiDocumentTests.cs`, `ApiResponseTimeTests.cs`, `InMemoryTodoRepositoryTests.cs` and `TodoItemDto.cs`;
  - indentation of a method in `ApiResponseTimeTests.cs` and of the `CreateItem` initializer in `InMemoryTodoRepositoryTests.cs`;
  - missing final newlines in `ApiResponseTimeTests.cs`, `CreateRepeatingTodoItemRequest.cs` and its tests.

🤖 Generated by `/issue-to-pr` with [Claude Code](https://claude.com/claude-code)

Closes #53
