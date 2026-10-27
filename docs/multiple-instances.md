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
| `JsonFileTodoRepositoryTests` | Two repositories over one file stand for two instances: changes are visible through the other one, concurrent writes lose no task, the lock is waited for |
| `InstanceIdentityTests` | The name from configuration and its default; two API hosts return different names in `GET /api/info`; the `X-Instance-Id` header on `200`, `404` and `/health` |
| `OpenApiDocumentTests` | `X-Instance-Id` is documented as a required header of every response, and real responses send it |

The shared log file is not covered by an automated test: two API hosts inside one test process share Serilog's static logger, so such a test would not show what happens with separate processes. It was checked by hand with `scripts/run-instances.sh`.
