# Run of /issue-to-pr #53

- 08:42:22 | preflight | ok
- 08:42:32 | ingest | 0VP-T1-20 Benchmark: Add user identification to tasks
- 08:50:16 | plan | 23 files
- 08:50:26 | branch | feat/t1-53-benchmark-add-user-identification-to
- 09:02:41 | implement | 3 commits

## Deviations from the plan

- `README.md`, `docs/openapi.md`, `docs/persistence.md`, `docs/multiple-instances.md` were not changed, and the plan's 4th commit `docs(api)` was not made: the implementer is limited to `src/` and `tests/`. The release agent must carry out the plan's *Documentation* section.
- `tests/DTO/CreateTodoItemRequestTests.cs` and `tests/DTO/CreateRepeatingTodoItemRequestTests.cs`: `Validate_MissingUserId_FailsOnUserId` builds the request by deserializing JSON (web defaults) instead of using an object initializer, to avoid a new `!` when assigning `null` to the non-nullable `UserId`. It covers `null`, `""`, `"   "` and a missing field.
- `tests/Data/JsonFileTodoRepositoryTests.cs`: added an optional `userId` parameter to the `CreateItem` helper (the plan mentions this only for the in-memory and controller tests). `LegacyFileWithoutUserId_TasksSurviveAWrite` also checks that the file contains `"userId": null` after the write.
- `tests/OpenApi/OpenApiDocumentTests.cs`: added a small `LoadSchemaAsync` helper so the new tests need no `!`.
- Whitespace only (`dotnet format whitespace --include` on touched files): trailing whitespace in `OpenApiDocumentTests.cs`, `ApiResponseTimeTests.cs`, `InMemoryTodoRepositoryTests.cs` and `TodoItemDto.cs`; a wrongly indented method in `ApiResponseTimeTests.cs`; indentation of the `CreateItem` initializer in `InMemoryTodoRepositoryTests.cs`; missing final newlines in `ApiResponseTimeTests.cs`, `CreateRepeatingTodoItemRequest.cs` and its tests.

Commits:
- 18e8c14 feat(dto): require userId when creating a task
- 66bc7d9 feat(data): store the task owner in TodoItem
- 666df44 feat(api): return the task owner and keep it fixed after creation

## Verification

- status: passed
- first_attempt_passed: yes
- repair_cycles: 0
- tests: total=347 passed=345 failed=0 skipped=2
- repair commits: none

## Log (continued)

- 09:03:18 | verify | passed, 0 repair cycles
- 09:06:33 | release | https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/59
