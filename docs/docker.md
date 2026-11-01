# Docker

The API can run in Docker as three named API instances sharing one persistent JSON storage volume.

The default Compose configuration starts the services `api-1`, `api-2`, and `api-3`. Each service listens on port `8080` inside its container and has a fixed host port:

- `api-1` → `http://localhost:5001`
- `api-2` → `http://localhost:5002`
- `api-3` → `http://localhost:5003`

## Build and start

Build the API image and start all configured services:

```powershell
docker compose up --build -d
```

Check the running containers:

```powershell
docker compose ps
```

All three API containers should eventually report a `healthy` status.

## Instance identity

Each API service has an explicit instance name:

- `api-1`
- `api-2`
- `api-3`

To check which instance answered a request:

```powershell
Invoke-RestMethod http://localhost:5001/api/info
Invoke-RestMethod http://localhost:5002/api/info
Invoke-RestMethod http://localhost:5003/api/info
```

The `instanceName` values should be `api-1`, `api-2`, and `api-3` respectively. Each container also has its own `machineName`.

## Shared task storage

All three API services use the same Docker named volume mounted at:

```text
/app/data
```

The JSON task file is configured through:

```text
Todo__Storage__FilePath=/app/data/todos.json
```

This means a task created through one API instance is visible through the others.

For example, a task can be created through port `5001` and then retrieved through port `5003`.

The task data is stored in the Docker-managed named volume `todo-data`, not in the repository working directory.

The application is configured with sample-data seeding disabled in Docker:

```text
Todo__SeedSampleData=false
```

Therefore, a newly created or reset data volume starts with an empty task store.

## Logs

Persistent application logs are stored in the Docker named volume `api-logs`, mounted at:

```text
/app/logs
```

Each API service writes to its own log file:

```text
api-1 → /app/logs/api-1-.log
api-2 → /app/logs/api-2-.log
api-3 → /app/logs/api-3-.log
```

The files use daily rolling, so the actual filenames include the date, for example:

```text
api-1-20261101.log
api-2-20261101.log
api-3-20261101.log
```

Using a separate file per service avoids multiple container processes writing concurrently to the same physical log file.

The logs are stored in a Docker named volume, so they are not normally visible as files in the repository working directory.

To inspect the log directory from a running service:

```powershell
docker compose exec api-1 sh -c "ls -la /app/logs"
```

## Number of instances

The Compose configuration explicitly defines three named API services: `api-1`, `api-2`, and `api-3`.

The setup does not use `docker compose --scale` because each service has its own fixed host port, instance name, and log file path.

If the number of instances needs to change, add or remove named services in `docker-compose.yml` and configure a unique host port, `Instance__Name`, and `PersistentLogging__FilePath` for each instance.

All instances should continue to use the same `todo-data` volume so they share the same task storage.

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