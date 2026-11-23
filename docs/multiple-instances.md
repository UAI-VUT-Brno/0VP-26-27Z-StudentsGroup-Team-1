# Running multiple instances

Several instances of the API can run side by side on one machine. They share one task file, so a task created through one instance is returned by all the others. This is the local counterpart of running several replicas of the container behind a load balancer.

## Quick start

From the repository root, in Git Bash (Windows), WSL, Linux or macOS:

```bash
./scripts/run-instances.sh
```

The script builds the API and starts three instances:

| Instance | URL |
|---|---|
| `api-5001` | `http://localhost:5001` |
| `api-5002` | `http://localhost:5002` |
| `api-5003` | `http://localhost:5003` |

Pass a number to start a different count, for example `./scripts/run-instances.sh 5` for ports 5001–5005. Ctrl+C stops all of them.

The instances run in the `Development` environment, so Swagger UI is available on each port at `/swagger`.

## Starting an instance by hand

Each instance needs its own port. The port is set with `--urls` (or the `ASPNETCORE_URLS` variable); the instance name with `Instance__Name`:

```bash
Instance__Name=api-5001 dotnet run --project src/UAI.ZeroVP.API -- --urls http://localhost:5001
```

In PowerShell:

```powershell
$env:Instance__Name = "api-5001"; dotnet run --project src/UAI.ZeroVP.API -- --urls http://localhost:5001
```

Run the command in a separate terminal for every instance, with a different port and name.

## Checking that the instances share data

```bash
# every instance answers with its own name
curl http://localhost:5001/api/info
curl http://localhost:5002/api/info

# create a task through instance 5001 ...
curl -X POST http://localhost:5001/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Created through 5001"}'

# ... and read it through instance 5003
curl http://localhost:5003/api/todos
```

`todoCount` in `GET /api/info` is the same on every instance, because it is read from the shared file.

## Instance identity

| Setting | Default | Meaning |
|---|---|---|
| `Instance:Name` (`Instance__Name`) | `<machine name>-<process ID>` | Name of the instance. In a container the machine name is the container ID, so the default already differs between replicas. |

The name appears in three places:

- `instanceName` in the response of `GET /api/info`,
- the `X-Instance-Id` header of every response, errors included,
- every log line, as `[api-5001]`.

```text
$ curl -i http://localhost:5002/api/info
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Instance-Id: api-5002
...
{"instanceName":"api-5002","machineName":"LAPTOP-1234","environment":"Development",...,"todoCount":5}
```

## Shared data

All instances use the file from `Todo:Storage:FilePath` – by default `src/UAI.ZeroVP.API/data/todos.json` when started from this repository. Every operation locks the file, reads it, applies the change and writes it back, so concurrent writes from several instances do not overwrite each other. The details, including the lock file `todos.json.lock`, are in [persistence.md](persistence.md#locking).

To start from scratch, stop all instances and delete the `data` folder.

## Concurrent changes to one task

The lock stops instances from losing each other's writes to the file, but on its own it would not stop a client from overwriting a change it has not seen. Every task therefore has a `version`, and `PUT` and `DELETE` must send the version the client last read. A successful write through one instance raises the version in the shared file, so the older version becomes stale on **all** instances: a `PUT` or `DELETE` with it, sent to any instance, gets `409 Conflict` and changes nothing. The client reads the task again, from any instance, and retries with the new version.

```bash
# both instances return the task with "version": 1
curl http://localhost:5001/api/todos/<id>
curl http://localhost:5002/api/todos/<id>

# an update through 5001 with version 1 succeeds and returns "version": 2
curl -X PUT http://localhost:5001/api/todos/<id> \
  -H "Content-Type: application/json" \
  -d '{"title":"Changed through 5001","version":1}'

# the same version through 5002 is now stale: 409 Conflict, detail names version 2
curl -i -X PUT http://localhost:5002/api/todos/<id> \
  -H "Content-Type: application/json" \
  -d '{"title":"Changed through 5002","version":1}'
```

The version check and the write run under the same file lock, so of two instances writing with the same version at the same moment, exactly one succeeds. See [persistence.md](persistence.md#optimistic-concurrency).

## Shared log file

All instances also write into the same log file, `src/UAI.ZeroVP.API/logs/uai-zerovp-<date>.log`. The file is opened in shared mode, so no instance locks the others out, and every line carries the instance name:

```text
2026-10-27 15:20:52.101 +01:00 [INF] [api-5002] [Serilog.AspNetCore.RequestLoggingMiddleware] HTTP POST /api/todos responded 201 in 4.8833 ms
2026-10-27 15:20:52.106 +01:00 [INF] [api-5003] [Serilog.AspNetCore.RequestLoggingMiddleware] HTTP POST /api/todos responded 201 in 6.1873 ms
```

To get one file per instance instead, give each instance its own `PersistentLogging__FilePath`, for example `logs/api-5001-.log`.

## Tests

| Test class | What it covers |
|---|---|
| `JsonFileTodoRepositoryTests` | Two repositories over one file stand for two instances: changes are visible through the other one, concurrent writes lose no task, the lock is waited for; a version-checked write through one makes the version stale on the other, and of concurrent writes with the same version from both, exactly one succeeds |
| `PersistentApiIntegrationTests` | Two API hosts over one file: a task created through one is visible through the other; a successful `PUT` through one host makes the version stale on the other (`409` naming the new version, the other host then sees the first host's change and can update with the new version); a stale `DELETE` returns `409` and keeps the task |
| `InstanceIdentityTests` | The name from configuration and its default; two API hosts return different names in `GET /api/info`; the `X-Instance-Id` header on `200`, `404` and `/health` |
| `OpenApiDocumentTests` | `X-Instance-Id` is documented as a required header of every response, and real responses send it |

The shared log file is not covered by an automated test: two API hosts inside one test process share Serilog's static logger, so such a test would not show what happens with separate processes. It was checked by hand with `scripts/run-instances.sh`.
