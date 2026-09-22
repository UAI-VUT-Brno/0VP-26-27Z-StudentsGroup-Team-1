# C1 - Where the application gets its settings

## Goal

Find out everything that influences the application's behaviour inside a container
*before* you start changing anything. Without this map, debugging configuration is
guesswork.

## Assignment

1. Build the image `zerovp:c01`.
2. Find out **which settings files** made it into the image.
3. Print the contents of `appsettings.json` inside the image without running the
   application.
4. Run a container and record the default state: environment, availability of
   `/swagger`, number of sample todos.
5. List every environment variable inside the running container and find the ones
   set by Microsoft's base image.
6. Explain why `/swagger` does not work even though it did on your developer machine.

---

## Solution

### 1. Building

```bash
docker build -f case-study-c-configuration/01-baseline/Dockerfile -t zerovp:c01 .
```

### 2. Settings files in the image

```bash
docker run --rm --entrypoint sh zerovp:c01 -c "ls -1 /app/appsettings*.json"
```

```
/app/appsettings.Development.json
/app/appsettings.json
```

Both files came from `dotnet publish`. **They are part of the image** - whoever has
the image has them. No passwords and no connection strings belong in them.

### 3. Contents without running the application

```bash
docker run --rm --entrypoint cat zerovp:c01 /app/appsettings.json
```

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "Todo": { "SeedSampleData": true },
  "Swagger": { "Enabled": false }
}
```

The `--entrypoint` switch replaces what is meant to run. Without it the arguments
would simply be appended to `dotnet UAI.ZeroVP.API.dll` and the application would start.

> On Git Bash under Windows this command fails with
> `cat: 'C:/Program Files/Git/app/appsettings.json'`. Prefix it with `MSYS_NO_PATHCONV=1`.

### 4. The default state

```bash
docker run -d --name cfg -p 8080:8080 zerovp:c01

curl http://localhost:8080/api/info
curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
```

| What | Value |
|---|---|
| `environment` | `Production` |
| `/swagger` | `404` |
| `todoCount` | `4` |

### 5. Environment variables inside

```bash
docker exec cfg env | sort
```

```
APP_UID=1654
ASPNETCORE_ENVIRONMENT=Production        <-- our Dockerfile
ASPNETCORE_HTTP_PORTS=8080               <-- Microsoft's base image
DOTNET_RUNNING_IN_CONTAINER=true         <-- Microsoft's base image
DOTNET_VERSION=10.0.12
ASPNET_VERSION=10.0.12
HOSTNAME=1b2c3d4e5f6a
PATH=...
```

Worth noting:

- **`ASPNETCORE_HTTP_PORTS=8080`** is set by Microsoft in all its .NET 8+ images.
  That is why the application in a container listens on 8080 without you writing it
  anywhere.
- **`DOTNET_RUNNING_IN_CONTAINER=true`** is how .NET knows it is containerised.
  Among other things it derives memory limits for garbage collection from that.
- **`ASPNETCORE_ENVIRONMENT=Production`** comes from our Dockerfile.

### 6. Why `/swagger` does not work

Two things combine.

**First, the environment is `Production`.** When you ran the application with
`dotnet run`, the environment was set by `Properties/launchSettings.json` to
`Development`. That file is used **only by `dotnet run` on a developer machine** -
it never reaches the published application, and therefore never reaches the image.
It does not apply in a container.

**Second, `appsettings.json` contains `"Swagger": { "Enabled": false }`.** It is
switched on only in `appsettings.Development.json`, which is not loaded in the
`Production` environment.

The application loads files in this order:

```
appsettings.json                              always
appsettings.<ASPNETCORE_ENVIRONMENT>.json     only for that environment
```

In `Production` it therefore loads `appsettings.json` and looks for
`appsettings.Production.json`, which is **not in the image**. That is intentional -
we supply it from outside in example 3.

### Cleaning up

```bash
docker rm -f cfg
```

---

## Map of configuration sources

For completeness now, in detail in example 3. Ordered from weakest to strongest:

| # | Source | Where you work with it |
|---|---|---|
| 1 | `appsettings.json` | inside the image, fixed |
| 2 | `appsettings.<Environment>.json` | inside the image or mounted from outside |
| 3 | environment variables | `-e`, `--env-file`, `ENV` in the Dockerfile |
| 4 | command-line arguments | after the image name in `docker run` |

**A higher number beats a lower one.**

---

## Takeaways

- `launchSettings.json` does not apply in a container. You have to set the
  environment yourself.
- `appsettings*.json` files are part of the image. Secrets do not belong in them.
- `--entrypoint` is how you look inside an image without starting the application.
- Microsoft's base images already set `ASPNETCORE_HTTP_PORTS` and other variables
  for you. It pays to know which - `docker exec <name> env | sort`.
