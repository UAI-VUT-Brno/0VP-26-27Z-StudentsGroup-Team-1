# Testing rules

## Suite

- One test project: `tests/UAI.ZeroVP.Tests.csproj` with xUnit 2, using only `Assert` (no assertion libraries).
- Run the suite with `dotnet test --configuration Release`. CI runs the same command.
- The suite on `main` has 327 test cases: 325 pass and 2 are skipped. **All of them must stay green.**
- Two performance tests are skipped unless `RUN_PERFORMANCE_TESTS=true`. Leave them skipped.

## Where tests go

| Folder | What | How |
|---|---|---|
| `tests/DTO/` | DataAnnotations validation, JSON converters | `ValidationHelper` |
| `tests/Data/` | `InMemoryTodoRepository`, `JsonFileTodoRepository`, `JsonFileTodoStore`, DI registration | direct calls. For the JSON file, use a temporary directory and delete it afterwards |
| `tests/API/` unit | controllers called directly | `new TodosController(repository, FakeTimeProvider, NullLogger<T>.Instance)` over a real `InMemoryTodoRepository` with `SeedSampleData = false` |
| `tests/API/` integration | real HTTP | `IClassFixture<ApiTestFactory>` (in memory) or `PersistentApiTestFactory` (JSON file) |
| `tests/OpenApi/` | the generated OpenAPI document | `OpenApiTestFactory` |

- Namespace: `UAI.ZeroVP.Tests.<Folder>`.
- Class name: `<Subject>Tests`.
- Method name: `Method_Scenario_ExpectedResult`, for example `GetAll_UnknownUser_ReturnsEmptyList`.
- Test data and comments are in Czech, like the existing tests.
- Use `FakeTimeProvider` for time and `FakeLogger<T>` to assert on logs. Never use `Thread.Sleep` or real time.

## What to test

- **Every acceptance criterion maps to at least one test.** Name the criterion in a comment above the test.
- A new endpoint needs:
  - a controller unit test;
  - an HTTP integration test (status code, body shape, `Content-Type`);
  - an OpenAPI test if the endpoint changes the document (responses, parameters, schemas).
- A new repository method needs tests for **both** implementations.
- Test the edge cases next to the happy path: empty filters, unknown IDs (`404`), invalid input (`400`), case sensitivity of `userId`.
- A change to persisted data needs a test that reads a file written **before** the change.

## Never

- Never delete, skip (`Skip =`), comment out or weaken a test to make the suite pass.
- Never change an assertion in an **existing** test, unless the issue explicitly changes that behaviour. If it does, say so in the commit message and the PR.
- Never add `Thread.Sleep`, retries or longer timeouts to hide a race. Fix the race.
