# Task persistence

Tasks survive a restart of the API or its container. They are stored in a JSON file.

## How it works

At runtime the tasks still live in memory (`InMemoryTodoRepository`). The JSON file is used at two moments only:

- **Start**: the tasks are loaded from the file before the API accepts its first request.
- **Stop**: all tasks are written to the file when the application shuts down gracefully (Ctrl+C, `SIGTERM`, `docker stop`).

The file is not written after every change. If the process is killed without a graceful shutdown (`kill -9`, power loss, crash), changes since the last start are lost. As agreed in the assignment, such failures are not handled for now.

The file is written to a temporary file `todos.json.tmp` first, which then replaces `todos.json`. An interrupted write therefore never leaves a half-written `todos.json`.

The implementation is in `src/UAI.ZeroVP.Data/JsonFile/`:

| Class | Responsibility |
|---|---|
| `JsonFileTodoStore` | Reads and writes the file, handles a missing and a corrupted file |
| `JsonFileTodoPersistenceService` | Hosted service: loads at start, saves at stop |
| `JsonFileTodoOptions` | Configuration (`Todo:Storage:FilePath`) |

The registration `AddJsonFileTodoData` in `DataServiceCollectionExtensions` puts it together. Controllers still depend only on `ITodoRepository` and did not change.

## Start-up cases

| File state | What happens | Log |
|---|---|---|
| Exists and is valid | Tasks are loaded; sample data is **not** added, even if the file holds an empty list | `Information`: `Loaded N tasks from …` |
| Does not exist | Start from scratch: four sample tasks if `Todo:SeedSampleData` is `true`, otherwise empty | `Information`: `Task file … does not exist, starting from scratch.` |
| Corrupted | The file is renamed to `todos.json.corrupt-<UTC timestamp>` and the API starts from scratch as above | `Error`: `Task file … is corrupted and was moved to …` with the reason |

A file counts as corrupted when it is not valid JSON, is not a list of tasks, contains `null` instead of a task, misses a required field (`id`, `title`, `createdAtUtc`), has `null` in `title`, has an unknown `priority`, or contains the same `id` twice.

The corrupted file is moved aside rather than overwritten, so it can be inspected or repaired by hand later.

## Configuration

| Setting | Default | Meaning |
|---|---|---|
| `Todo:Storage:FilePath` | `data/todos.json` | Path to the file. A relative path is resolved from the application content root. An empty value turns persistence off and the API keeps data in memory only (the behaviour before this change). |
| `Todo:SeedSampleData` | `true` | Add four sample tasks when starting from scratch. |

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
    "updatedAtUtc": "2026-10-25T13:00:58.762722+00:00"
  }
]
```

`userId` was added later. A file saved before that has no `userId`; such tasks still load and have `userId: null`.

## Tests

Tests never touch `src/UAI.ZeroVP.API/data/`. The API test factories set `Todo:Storage:FilePath` to an empty value, and the data-layer tests use a temporary folder that is deleted after each test.

| Test class | What it covers |
|---|---|
| `JsonFileTodoStoreTests` | Save and load of all fields, missing file, corrupted file variants, backup of the corrupted file, atomic overwrite |
| `JsonFileTodoDataTests` | DI registration; a host is stopped and a new host on the same file loads the tasks (restart), changes and deletions survive, a corrupted file at start |
| `InMemoryTodoRepositoryTests` | The repository starts from given items instead of sample data |

## Multiple instances

This persistence is designed for one instance. Several instances sharing one file would each overwrite it on shutdown with their own data. Sharing data between instances is task 0VP-T1-09.
