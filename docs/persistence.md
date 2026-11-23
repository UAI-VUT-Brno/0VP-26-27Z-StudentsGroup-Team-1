# Task persistence

Tasks survive a restart of the API or its container. They are stored in a JSON file.

## How it works

The file is the single source of truth, so several API instances can share it. Every repository operation:

1. locks the file (see [Locking](#locking)),
2. reads the current tasks from it,
3. runs the operation on them,
4. writes the file back if the operation changed something,
5. releases the lock.

A change is therefore in the file as soon as the request returns, and another instance sees it on its next request. Nothing is lost when the process is killed (`kill -9`, crash), apart from the request that was running at that moment.

The file is written to a temporary file `todos.json.tmp` first, which then replaces `todos.json`. An interrupted write therefore never leaves a half-written `todos.json`.

The implementation is in `src/UAI.ZeroVP.Data/JsonFile/`:

| Class | Responsibility |
|---|---|
| `JsonFileTodoRepository` | `ITodoRepository` over the file: lock, read, run the operation, write. The operation itself (filter, ordering, all-or-nothing batch) is done by a short-lived `InMemoryTodoRepository` over the loaded tasks. |
| `JsonFileTodoStore` | Reads and writes the file, handles a missing and a corrupted file |
| `JsonFileTodoOptions` | Configuration (`Todo:Storage:FilePath`, lock timeout) |

The registration `AddJsonFileTodoData` in `DataServiceCollectionExtensions` puts it together. Controllers still depend only on `ITodoRepository` and did not change.

## File states

The file is checked on every operation, not only at start-up.

| File state | What happens | Log |
|---|---|---|
| Exists and is valid | Tasks are loaded; sample data is **not** added, even if the file holds an empty list | `Debug`: `Loaded N tasks from …` |
| Does not exist | Start from scratch: four sample tasks if `Todo:SeedSampleData` is `true`, otherwise empty. The file is created right away, even by a read, so all instances see the same sample tasks. | `Information`: `Task file … does not exist, starting from scratch.` |
| Corrupted | The file is renamed to `todos.json.corrupt-<UTC timestamp>` and the API starts from scratch as above | `Error`: `Task file … is corrupted and was moved to …` with the reason |

A file counts as corrupted when it is not valid JSON, is not a list of tasks, contains `null` instead of a task, misses a required field (`id`, `title`, `createdAtUtc`), has `null` in `title`, has an unknown `priority`, or contains the same `id` twice.

The corrupted file is moved aside rather than overwritten, so it can be inspected or repaired by hand later.

## Configuration

| Setting | Default | Meaning |
|---|---|---|
| `Todo:Storage:FilePath` | `data/todos.json` | Path to the file. A relative path is resolved from the application content root. An empty value turns persistence off and the API keeps data in memory only (the behaviour before this change). |
| `Todo:SeedSampleData` | `true` | Add four sample tasks when starting from scratch. |
| `JsonFileTodoOptions.LockTimeout` | 10 s | How long an operation waits for another instance to release the file before it fails with `TimeoutException` (HTTP 500). Set in code only. |

When the API runs locally from this repository, the file is therefore `src/UAI.ZeroVP.API/data/todos.json`. The folder is ignored by Git.

Both settings can be overridden with environment variables, for example in Docker:

    -e Todo__Storage__FilePath=/data/todos.json
    -v uai-zerovp-data:/data

## File format

The file uses the same conventions as the API: camelCase property names, the priority as a string, and dates in ISO 8601 with an offset. It is indented and keeps diacritics as they are, so it can be read and edited by hand.

```json
[
  {
    "id": "f7cb0a24-6f9e-4c54-9b3e-d17843f16b15",
    "userId": "demo",
    "title": "Napsat Dockerfile",
    "description": "Vícefázový build nad SDK a runtime image.",
    "priority": "High",
    "isCompleted": false,
    "dueAtUtc": null,
    "createdAtUtc": "2026-10-25T13:00:58.762722+00:00",
    "completedAtUtc": null,
    "updatedAtUtc": "2026-10-25T13:00:58.762722+00:00",
    "version": 1
  }
]
```

`userId` was added later. A file saved before that has no `userId`; such tasks still load and have `userId: null`.

`version` was added later too. A file saved before that has no `version`; such tasks load with version 1, and the next write adds `version` to the file. No migration is needed. Older builds can still read the new file, because the serializer ignores unknown properties.

## Optimistic concurrency

Every task has a `version`. New tasks start at 1, and every successful write raises it by exactly 1. `PUT` and `DELETE` carry the version the client last read. `UpdateIfVersionMatchesAsync` and `DeleteIfVersionMatchesAsync` compare it with the stored version and write in the same `ExecuteAsync` call, so the comparison and the write run inside one locked read–change–write cycle (see [Locking](#locking)). Another instance cannot write between the two steps. When the versions differ, the repository returns a conflict, the data stay as they are and the API answers `409 Conflict`.

Like any other write operation, a version-checked write saves the file even when the result is a conflict or "not found". The file content is then unchanged.

`POST /api/todos/{id}/complete` does not take a version from the client. It uses the version it has just read; if another write got in between, it reads the task again and completes the latest state, so it never overwrites a concurrent change.

## Tests

Tests never touch `src/UAI.ZeroVP.API/data/`. The API test factories set `Todo:Storage:FilePath` to an empty value, and the data-layer tests use a temporary folder that is deleted after each test.

| Test class | What it covers |
|---|---|
| `JsonFileTodoStoreTests` | Save and load of all fields including `version`, a file from before versions loads with version 1, missing file, corrupted file variants, backup of the corrupted file, atomic overwrite |
| `JsonFileTodoRepositoryTests` | Two repositories over one file (= two instances): changes are visible through the other one, concurrent writes lose no task, sample data are created once, a failed write leaves the file unchanged, waiting for and timing out on the lock; a version-checked write through one makes the version stale on the other, a stale delete keeps the task, of concurrent writes with the same version exactly one succeeds |
| `JsonFileTodoDataTests` | DI registration; a change is in the file immediately; a host is stopped and a new host on the same file loads the tasks (restart), changes and deletions survive, a corrupted file at start |
| `InMemoryTodoRepositoryTests` | The repository starts from given items instead of sample data; version-checked update and delete (matching version, stale version, unknown task), of 1000 concurrent updates with the same version exactly one succeeds |
| `PersistentApiIntegrationTests` | API hosts over one file; a file from before versions loads with version 1 and accepts an update that writes `"version": 2` |

## Locking

Several instances (processes) may use the file at the same time, so every operation holds a lock for the whole read–change–write cycle:

- **Between processes**: the lock file `todos.json.lock` next to the data file, opened with `FileShare.None`. While one instance has it open, others cannot open it and retry every 10 ms. The lock file is never deleted; deleting it could let two instances in at once.
- **Inside one process**: a `SemaphoreSlim` in the repository, so concurrent requests of one instance queue up instead of polling the lock file.

Reads hold the lock too, so they never see a file that is being replaced.
