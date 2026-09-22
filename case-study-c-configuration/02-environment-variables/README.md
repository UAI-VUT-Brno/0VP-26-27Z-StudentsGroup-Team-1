# C2 - Environment variables

## Goal

Change the application's behaviour without touching the image. Environment
variables are the most common way to do that - and they have rules you need to know.

This example needs no Dockerfile of its own; it uses `zerovp:c01` from example 1.

## Assignment

1. Turn Swagger on without changing the image or the environment.
2. Switch the container into the `Development` environment and watch what happens
   to Swagger.
3. Run the container in `Development` but with **Swagger turned off**.
   Explain which configuration source won.
4. Turn off the sample-data seeding.
5. Work out how the key `Logging:LogLevel:Default` is written as an environment variable.
6. Explain the difference between `ENV` in a Dockerfile and `-e` at `docker run` time.

---

## Solution

### How a settings key becomes a variable

Configuration in .NET is a tree. In JSON:

```json
{ "Swagger": { "Enabled": true } }
```

Code refers to it as `Swagger:Enabled`. A colon is not valid in environment
variables on every system, so it is replaced by a **double underscore**:

```
Swagger__Enabled=true
```

| Settings key | Environment variable |
|---|---|
| `Swagger:Enabled` | `Swagger__Enabled` |
| `Todo:SeedSampleData` | `Todo__SeedSampleData` |
| `Logging:LogLevel:Default` | `Logging__LogLevel__Default` |
| `ConnectionStrings:Main` | `ConnectionStrings__Main` |

**A single underscore is not enough.** `Swagger_Enabled` is treated as one key of
that literal name and the application never notices it. This is the single most
common mistake in this whole area - and nothing reports it, the setting simply has
no effect.

### 1. Turning Swagger on

```bash
docker run -d --name cfg -p 8080:8080 \
    -e Swagger__Enabled=true \
    zerovp:c01

curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 200
docker rm -f cfg
```

The environment stayed `Production` and the image is unchanged. One variable moved.

### 2. Switching the environment

```bash
docker run -d --name cfg -p 8080:8080 \
    -e ASPNETCORE_ENVIRONMENT=Development \
    zerovp:c01

curl http://localhost:8080/api/info
# "environment":"Development"
curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 200
docker rm -f cfg
```

Swagger came up without you switching it on. The reason: in the `Development`
environment the file `appsettings.Development.json` is also loaded, and it is in
the image and contains `"Swagger": { "Enabled": true }`.

> Do not flip the environment like this in production. `Development` enables
> detailed error pages with stack traces and changes the behaviour of other
> components. To enable Swagger use `Swagger__Enabled=true` - that is exactly what
> the switch is there for.

### 3. Development, but Swagger off

```bash
docker run -d --name cfg -p 8080:8080 \
    -e ASPNETCORE_ENVIRONMENT=Development \
    -e Swagger__Enabled=false \
    zerovp:c01

curl -o /dev/null -w "%{http_code}\n" http://localhost:8080/swagger/index.html
# 404
docker rm -f cfg
```

**The environment variable overrode `appsettings.Development.json`.** This is the
heart of .NET configuration: sources stack in order and a later one overwrites an
earlier one. Files are at the bottom, environment variables above them.

### 4. Turning off the sample data

```bash
docker run -d --name cfg -p 8080:8080 \
    -e Todo__SeedSampleData=false \
    zerovp:c01

curl http://localhost:8080/api/info
# "todoCount":0
docker rm -f cfg
```

### 5. Multi-level keys

```bash
docker run -d --name cfg -p 8080:8080 \
    -e Logging__LogLevel__Default=Debug \
    -e Logging__LogLevel__Microsoft.AspNetCore=Information \
    zerovp:c01

docker logs cfg | head -20
docker rm -f cfg
```

Each level of nesting is one double underscore. A dot inside a name
(`Microsoft.AspNetCore`) stays a dot - it is part of the key, not a separator.

### 6. `ENV` in a Dockerfile vs `-e` at run time

| | `ENV` in the Dockerfile | `-e` at `docker run` |
|---|---|---|
| When it applies | baked into the image | only when the container starts |
| Who sees it | anyone with the image (`docker inspect`) | only whoever runs it |
| Changing it requires | rebuilding the image | nothing, just a different command |
| What it is for | **sensible defaults** | **environment differences and secrets** |

Confirm that `-e` beats `ENV`:

```bash
# the Dockerfile has ENV ASPNETCORE_ENVIRONMENT=Production
docker run -d --name cfg -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Staging zerovp:c01

curl http://localhost:8080/api/info
# "environment":"Staging"      <-- -e won over ENV
```

And that `ENV` is visible in the image to anyone:

```bash
docker inspect zerovp:c01 --format "{{json .Config.Env}}"
```

**Hence an important rule: never put passwords, tokens or connection strings in
`ENV`.** They stay in the image, in all its layers and in the registry, and cannot
be removed from there afterwards. Secrets belong at start-up time - through `-e`,
`--env-file`, or better still `secrets` (example 5).

### Where variables are visible

Values passed with `-e` are not secret at run time either:

```bash
docker inspect cfg --format "{{json .Config.Env}}"     # anyone on the host sees them
docker exec cfg env                                    # and so does anyone inside
ps aux | grep docker                                   # and the process list at launch

docker rm -f cfg
```

Environment variables are convenient, not secure. Real secrets call for `secrets`
in Compose or a secret manager (Azure Key Vault, HashiCorp Vault).

---

## Takeaways

- The key `A:B:C` is written `A__B__C`. **Two underscores** - otherwise nothing
  happens and nothing reports it.
- Environment variables override `appsettings*.json` files.
- `ENV` in a Dockerfile is for defaults, `-e` for what differs between environments.
- Never bake secrets into an image. `ENV` is visible to anyone holding it.
- Do not experiment with `ASPNETCORE_ENVIRONMENT=Development` in production - it
  changes more than you expect.
