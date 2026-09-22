# C5 - Configuration in Docker Compose

## Goal

Put everything from the previous examples together: **one image, two environments,
no secrets inside the image**. And arrange it so the environments cannot be confused.

This folder holds:

| File | Contents |
|---|---|
| `compose.yaml` | two shapes of the same service, separated by profiles |
| `common.env` | settings shared by both environments |
| `development.env` | development only - Swagger on, verbose logging |
| `production.env` | production only - Swagger off |
| `database-password.txt` | a sample secret (it would not belong in version control) |

## Assignment

1. Find out which services start **without** naming a profile. Explain the result.
2. Start the development shape and check the environment, Swagger and the todo count.
3. Start the production shape and compare the same three.
4. In the production shape, find out **where the password lives inside the container**
   and why you cannot see it in `docker inspect` or in the environment listing.
5. Verify the production shape runs with a read-only filesystem, as an unprivileged
   user and with capped memory.
6. `common.env` sets `Todo__SeedSampleData=true`, yet the running container has zero
   todos. Find what overrode it.

---

## Solution

### 1. Profiles

```bash
docker compose config --services
# (empty output)

docker compose --profile dev config --services
# api-dev

docker compose --profile prod config --services
# api-prod
```

Both services carry `profiles:`, so **neither** starts unless asked for.

```yaml
api-dev:
  profiles: [dev]
```

That is deliberate: you cannot accidentally run `docker compose up` and get
something other than what you expected. The profile must always be named.

> A service **without** a `profiles` key always starts - with a profile and without
> one. That is how you write services common to every environment (a database, a queue).

### 2. The development shape

```bash
cd case-study-c-configuration/05-compose
docker compose --profile dev up -d --build

curl http://localhost:8080/api/info
curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
```

| What | Value |
|---|---|
| `environment` | `Development` |
| `/swagger` | `200` |
| `todoCount` | `4` |

```bash
docker compose --profile dev down
```

### 3. The production shape

```bash
docker compose --profile prod up -d --build

curl http://localhost:8080/api/info
curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
```

| What | Value |
|---|---|
| `environment` | `Production` |
| `/swagger` | `404` |
| `todoCount` | `0` |

**The same image, different behaviour.** Nothing was recompiled - `docker compose
config` would show both services carry `image: zerovp:c05`.

### 4. Where the password lives

```bash
docker compose --profile prod exec api-prod sh -c "cat /run/secrets/database-password"
# secret-password-that-does-not-belong-in-the-image

docker compose --profile prod exec api-prod ls -l /run/secrets/
# -rwxrwxrwx 1 root root 50 ... database-password
```

A secret reaches the container **as a file**, not as an environment variable:

```yaml
secrets:
  database-password:
    file: ./database-password.txt
```

The environment holds only the **path** to it:

```bash
docker inspect zerovp-c05-api-prod-1 --format "{{json .Config.Env}}" | tr "," "\n" | grep -i password
# "PasswordFilePath=/run/secrets/database-password"
```

The difference against an environment variable:

| | Environment variable | `secrets` |
|---|---|---|
| Visible to `docker inspect` | **yes, in full** | no, only the path |
| Visible to `env` inside | **yes** | no |
| Inherited by child processes | **yes** | no |
| Ends up in crash dumps | often yes | no |
| Where it lives | in the container configuration | a file under `/run/secrets/` |

In .NET such a file is read like this:

```csharp
var path = builder.Configuration["PasswordFilePath"];
var password = path is not null && File.Exists(path)
    ? File.ReadAllText(path).Trim()
    : builder.Configuration["ConnectionStrings:Main"];
```

> **`UAI.ZeroVP.API` does not contain those lines** - it has no database and nothing to
> use a password for. `PasswordFilePath` is here so the mechanism can be inspected from
> outside: the path shows up in `docker inspect`, the value does not. Step 4 checks
> exactly that, and it holds whether or not the application ever opens the file.

> **In plain Compose on a single machine** `secrets` is just a bind-mounted file -
> anyone with access to the host can read it. Real protection comes only with Docker
> Swarm or Kubernetes, where secrets travel encrypted. Writing it this way from the
> start still pays off: moving elsewhere then needs no change to the application.

### 5. Hardening

```bash
docker compose --profile prod exec api-prod touch /app/x
# touch: cannot touch '/app/x': Read-only file system

docker compose --profile prod exec api-prod touch /tmp/x
# succeeds - tmpfs

docker compose --profile prod exec api-prod id
# uid=1654(app) gid=1654(app) groups=1654(app)

docker stats --no-stream zerovp-c05-api-prod-1
# MEM USAGE / LIMIT: 25.28MiB / 256MiB
```

Four lines in `compose.yaml` take care of it:

```yaml
read_only: true
tmpfs:
  - /tmp
mem_limit: 256m
cpus: 0.5
```

The unprivileged account comes from the Dockerfile (`USER $APP_UID`) - Compose does
not have to deal with it.

### 6. What overrode the todo count

The sources stack in this order:

```
1. common.env        Todo__SeedSampleData=true       <-- env_file, first
2. production.env    (does not mention this key)     <-- env_file, second
3. environment:      Todo__SeedSampleData: "false"   <-- won
```

The rules that follow:

- **`env_file` entries are read in the order given** and a later file overrides an
  earlier one.
- **`environment` takes precedence over everything in `env_file`.**

You can confirm it without starting anything:

```bash
docker compose --profile prod config
```

Compose prints the resulting form with every value substituted. **This is the tool
for solving such a mystery** - not guessing over the files.

### Cleaning up

```bash
docker compose --profile prod down
docker compose --profile dev down
```

The profile has to be named for `down` too, otherwise Compose finds nothing to stop.

---

## Configuration options in Compose

| Form | What it does | Priority |
|---|---|---|
| `env_file: [a.env, b.env]` | loads variables from files, later overrides earlier | lower |
| `environment:` | variables directly in `compose.yaml` | higher |
| `secrets:` | a file at `/run/secrets/<name>` | outside the order |
| `volumes: ./x.json:/app/x.json:ro` | mounts a settings file | lowest |
| `profiles: [name]` | the service starts only with this profile | - |
| `x-something: &anchor` + `<<: *anchor` | a shared base for several services | - |

---

## Takeaways

- **Profiles** let the development and production shapes live in one file without
  any risk of confusing them. Without naming a profile such a service does not start.
- The order is: `volumes` with files -> `env_file` (in the order given) -> `environment`.
- **Secrets do not belong in `environment`.** `docker inspect` prints them for anyone
  on the host. Use `secrets` and read them from the file.
- `docker compose config` prints the resulting form with everything substituted.
  When a setting "does not work", start here.
- YAML anchors (`&anchor`, `<<: *anchor`) save repetition. Compose supports them fully.
