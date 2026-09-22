# C3 - Settings files and source precedence

## Goal

Supply a whole settings file to a container from outside and **confirm in practice
which source beats which**. This is the most important example of the case study.

Two prepared files are in this folder:

- `appsettings.Production.json` - added into the image at start-up
- `production.env` - a set of variables for `--env-file`

## Assignment

1. Mount `appsettings.Production.json` into the container and verify it took effect.
   Mount it read-only.
2. Add an environment variable claiming the opposite to the same mount.
   **Which source wins?**
3. Load settings from `production.env` with the `--env-file` switch.
4. Add a command-line argument that overrides one of the values from `--env-file`.
5. Assemble the complete source order from weakest to strongest and back it with
   measurements.
6. Decide which mechanism you would use for: defaults, environment differences,
   a database password, a one-off experiment while debugging.

---

## Solution

### 1. Mounting a file from outside

The image contains **no** `appsettings.Production.json` - and yet it is looked for
in the `Production` environment. We exploit that:

```bash
docker run -d --name cfg -p 8080:8080 \
    -v "K:/DockerTrivia/case-study-c-configuration/03-settings-files/appsettings.Production.json:/app/appsettings.Production.json:ro" \
    zerovp:c01

curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 200        <-- the file turned Swagger on
docker rm -f cfg
```

Breaking the `-v` switch down:

```
-v  K:/path/on/host.json : /app/appsettings.Production.json : ro
    |_ host source ____|   |_ container target ___________|   |_|
                                                          read-only
```

- **The host path must be absolute.** With `docker run`, a relative path is treated
  as the name of a named volume, which leads to confusing errors.
- **Always use `:ro` when the application is not supposed to write to the file.**
  Without it the container could overwrite the file on your disk.
- On Git Bash under Windows, prefix the command with `MSYS_NO_PATHCONV=1`, otherwise
  the in-container path is rewritten into nonsense.

> **Why mount individual files rather than a whole folder?** Mounting
> `-v ...:/app` would replace the entire contents of `/app`, compiled application
> included, and the container would not start. Mounting one file is a targeted change.

### 2. File versus environment variable

```bash
docker run -d --name cfg -p 8080:8080 \
    -v "K:/DockerTrivia/case-study-c-configuration/03-settings-files/appsettings.Production.json:/app/appsettings.Production.json:ro" \
    -e Swagger__Enabled=false \
    zerovp:c01

curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 404        <-- the environment variable won
docker rm -f cfg
```

**The environment variable overrode the mounted file.** Files sit lower in the
order regardless of whether they are in the image or mounted from outside.

### 3. A set of variables from a file

The contents of `production.env`:

```
ASPNETCORE_ENVIRONMENT=Production
Swagger__Enabled=false
Todo__SeedSampleData=false
```

```bash
docker run -d --name cfg -p 8080:8080 \
    --env-file case-study-c-configuration/03-settings-files/production.env \
    zerovp:c01

curl http://localhost:8080/api/info
# "todoCount":0
curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 404
docker rm -f cfg
```

Format rules people regularly get burned by:

- The form is `KEY=value`, one pair per line. Comments start with `#`.
- **Values are not interpreted by a shell.** Writing `PASSWORD="abc"` stores the
  quotes too, i.e. `"abc"`. Do not use them.
- Spaces around `=` become part of the name or the value. Do not write them.
- A reference to another variable (`PATH_X=$HOME/x`) is not expanded; it is stored
  literally.
- The path given to `--env-file` is relative to the directory you run the command from.

> `.env` files **do not belong in version control** if they hold secrets. Commit a
> template such as `production.env.example` with empty values instead.

### 4. A command-line argument

```bash
docker run -d --name cfg -p 8080:8080 \
    --env-file case-study-c-configuration/03-settings-files/production.env \
    zerovp:c01 \
    --Swagger:Enabled=true

curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 200        <-- the argument beat even the environment variable
curl http://localhost:8080/api/info
# "todoCount":0     <-- this value from the file survived
```

Everything after the image name is appended to `ENTRYPOINT`, so what actually runs is:

```
dotnet UAI.ZeroVP.API.dll --Swagger:Enabled=true
```

ASP.NET Core reads command-line arguments as the last configuration source, which is
why they win over everything else. Note that here you write a **colon**, not a double
underscore - that form applies to environment variables only.

### 5. The complete order - measured

| # | Source | Experiment | Result |
|---|---|---|---|
| 1 | `appsettings.json` in the image | default run | Swagger `404`, 4 todos |
| 2 | `appsettings.<Environment>.json` | mounted file with `Swagger:true` | Swagger `200` |
| 3 | environment variables | same file + `-e Swagger__Enabled=false` | Swagger `404` |
| 4 | command-line arguments | `--env-file` + `--Swagger:Enabled=true` | Swagger `200` |

**Every row beats all the rows above it.**

The practical consequence: when a setting "has no effect", do not look for a bug in
the application. Walk the table from the bottom and find what is overwriting the
value. And check the double underscore.

A quick way to see what the application really receives:

```bash
docker exec cfg env | sort | grep -iE "swagger|todo|aspnetcore"

docker rm -f cfg
```

### 6. What to use when

| Situation | Mechanism | Why |
|---|---|---|
| Sensible defaults | `appsettings.json` in the image | travels with the app, gets versioned |
| Differences between environments | mounted `appsettings.<Environment>.json` or `--env-file` | one image, many environments |
| Database password | `secrets` (example 5), a secret manager | belongs in neither the image nor `ENV` |
| One-off experiment while debugging | command-line argument | nothing permanent, highest priority |

---

## Takeaways

- The order is **files -> environment variables -> command-line arguments**.
  Higher always beats lower.
- Mount **individual files**, not whole folders. Mounting over `/app` breaks the app.
- `:ro` is the default choice, not the exception.
- In `--env-file`, quotes become part of the value. Do not use them.
- A key is written with `__` in an environment variable and with `:` in a
  command-line argument.
