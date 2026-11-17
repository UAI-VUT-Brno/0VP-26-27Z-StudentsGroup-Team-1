# Optimistic concurrency rules

Today `TodoItem` has **no** `version`, and the repository uses last-writer-wins. Follow these rules when an issue introduces a version, or when an entity already has one.

## Contract

- The entity `TodoItem` and the response `TodoItemDto` carry `Version` (`long`, serialized as `version`).
- A new task starts at `Version = 1`.
- Requests that change or delete a task carry the version the client last saw. Examples:
  - `version` in the body of `UpdateTodoItemRequest`;
  - a `version` query parameter for `DELETE` and `POST /complete`.
- A missing version is a validation error (`400`). It must not silently skip the check.

## Behaviour

| Situation | Response |
|---|---|
| Task does not exist | `404` ProblemDetails (existing `NotFoundTodo`) |
| Version matches | write succeeds, `Version` increases by exactly 1, `200` / `204` |
| Version does not match | `409 Conflict` ProblemDetails, nothing is written. `detail` names the current version |

## Where the check happens

- Compare and increment the version **inside the repository write**, never in the controller.
  - A check in the controller (read, compare, then write) is a race: another instance can write between the read and the write.
- `InMemoryTodoRepository` compares the stored version with the expected one inside its `TryUpdate` loop.
- `JsonFileTodoRepository` gets this for free, because each operation runs under the file lock inside one `ExecuteAsync`. Never split the check and the write into two `ExecuteAsync` calls.
- The repository must report three outcomes to the API: not found, conflict and success. Use a small result type or enum, not exceptions, and not `bool` alone.

## JSON file compatibility

- Tasks saved before versions existed have no `version` in the file. Load them as version `1`. Do not fail and do not drop them.
- Add a test that loads such a file.

## Tests

- update with the matching version → `200` and version + 1;
- update with a stale version → `409`, and the stored task is unchanged;
- delete with a stale version → `409`, and the task still exists;
- two sequential updates with the same original version → the first wins, the second gets `409`;
- the same scenarios through `PersistentApiTestFactory` (JSON file storage).
