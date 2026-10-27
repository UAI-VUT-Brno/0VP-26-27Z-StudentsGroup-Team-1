# Docker

The API can run in Docker as multiple replicas sharing one persistent JSON storage volume.

The default Compose configuration starts three API replicas. Each replica listens on port `8080` inside its container and is exposed locally through one of the host ports `5001`–`5003`.

## Build and start

Build the API image and start all configured replicas:

```powershell
docker compose up --build -d
```

Check the running containers:

```powershell
docker compose ps
```

All API containers should eventually report a `healthy` status.

The replicas are available locally at:

- `http://localhost:5001`
- `http://localhost:5002`
- `http://localhost:5003`

The ports are assigned from the configured range, so a particular replica number is not guaranteed to always receive the same host port.

## Replica identity

Each container has its own instance identity.

To check which replica answered a request:

```powershell
Invoke-RestMethod http://localhost:5001/api/info
Invoke-RestMethod http://localhost:5002/api/info
Invoke-RestMethod http://localhost:5003/api/info
```

The `instanceName` and `machineName` values should differ between replicas.

## Shared task storage

All API replicas use the same Docker named volume mounted at:

```text
/app/data
```

The JSON task file is configured through:

```text
Todo__Storage__FilePath=/app/data/todos.json
```

This means a task created through one replica is visible through the other replicas.

For example, a task can be created through port `5001` and then retrieved through port `5003`.

The task data is stored in the Docker-managed named volume `todo-data`, not in the repository working directory.

The application is configured with sample-data seeding disabled in Docker:

```text
Todo__SeedSampleData=false
```

Therefore, a newly created or reset data volume starts with an empty task store.

## Logs

Persistent application logs are written to:

```text
/app/logs
```

and stored in the Docker named volume `api-logs`.

The log path is configured through:

```text
PersistentLogging__FilePath=/app/logs/uai-zerovp-.log
```

## Scaling

The Compose configuration uses three replicas by default.

To explicitly run three replicas:

```powershell
docker compose up -d --scale api=3
```

The current published host-port range is `5001`–`5003`, so it supports up to three simultaneously exposed replicas.

If more replicas are required, extend the published port range in `docker-compose.yml` accordingly.

## Stop

Stop and remove the containers and Compose network while keeping persistent volumes:

```powershell
docker compose down
```

The task data and logs remain stored in their named volumes.

Starting the application again:

```powershell
docker compose up -d
```

reuses the existing volumes, so previously created tasks remain available.

## Reset persistent data

To stop the application and also remove its named volumes:

```powershell
docker compose down -v
```

This deletes both the task-data volume and the log volume.

The next start:

```powershell
docker compose up -d
```

creates fresh empty volumes.

Use `down -v` only when the stored Docker data should be intentionally reset.

## Inspect volumes

List Docker volumes:

```powershell
docker volume ls
```

The Compose project creates named volumes corresponding to:

```text
todo-data
api-logs
```

Docker normally prefixes their actual names with the Compose project name.

To see the fully resolved Compose configuration, including generated volume names, run:

```powershell
docker compose config
```

## Health check

The Docker image includes a health check against:

```text
/health
```

A direct health request can also be made from the host:

```powershell
Invoke-WebRequest http://localhost:5001/health -UseBasicParsing
```

A healthy API returns HTTP `200 OK`.

## Configuration

The Docker setup uses environment variables for runtime-specific configuration, including:

```text
Todo__Storage__FilePath
Todo__SeedSampleData
PersistentLogging__FilePath
Instance__Name
ASPNETCORE_ENVIRONMENT
```

ASP.NET Core maps double underscores (`__`) in environment-variable names to nested configuration sections.