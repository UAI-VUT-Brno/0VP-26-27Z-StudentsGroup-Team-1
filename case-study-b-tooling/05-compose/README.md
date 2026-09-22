# B5 - Docker Compose as a working tool

## Goal

Describe a whole development environment in one file and drive it with one tool.
This example is not only about `compose.yaml` syntax - mainly it shows how much
Compose does for you in day-to-day work.

The environment holds three services:

| Service | What it does | Port outward |
|---|---|---|
| `api` | our application, two replicas | internal 8080 only |
| `registry` | the image store from example 4 | 5555 |
| `nginx` | the single way into the environment | 8080 |

## Assignment

1. Check the syntax of `compose.yaml` without starting anything.
2. Look at what the file becomes once every variable is substituted.
3. Start the environment and watch the order in which the services come up.
4. Push an image into the registry running inside the same environment.
5. Stop the environment and start it again. Find out **what survived and what did
   not** - and why.
6. Run `docker compose watch`, change the source code and watch what happens.
7. Clean the environment up including volumes.

---

## Solution

### 1. Syntax check

```bash
cd case-study-b-tooling/05-compose
docker compose config --quiet && echo "looks fine"
```

Without `--quiet` it prints the whole file. Errors are reported with a line number
and nothing is started. A good first step on a build server.

### 2. The file after substitution

```bash
docker compose config
```

Compose prints the resulting file **with every variable substituted**, defaults
filled in and any override files merged. The line

```yaml
APP_VERSION: ${APP_VERSION:-1.4.2}
```

shows up as `APP_VERSION: "1.4.2"` when the variable is not set. The form
`${VARIABLE:-default}` means "take it from the environment, and if nothing is there,
use `default`".

Try it:

```bash
APP_VERSION=2.0.0 docker compose config | grep -A2 args
```

### 3. Starting up and service order

```bash
docker compose up -d --build
```

```
Container zerovp-b05-registry-1  Started
Container zerovp-b05-api-2       Healthy          <-- waits for the health check
Container zerovp-b05-api-1       Healthy
Container zerovp-b05-nginx-1     Started          <-- nginx only now
```

That order comes from this:

```yaml
depends_on:
  api:
    condition: service_healthy
```

**This is a substantial difference from a plain `depends_on: [api]`**, which only
waits for the container to start - that is, for the process to come up, not for it
to be ready to serve. With `service_healthy` Compose waits for the `HEALTHCHECK`
from the Dockerfile.

The available conditions:

| Condition | When it continues |
|---|---|
| `service_started` | as soon as the container runs (the default) |
| `service_healthy` | once the health check reports `healthy` |
| `service_completed_successfully` | once the service exits with code 0 (migrations, data prep) |

### 4. Pushing into the registry in the same environment

```bash
docker tag zerovp:b05 localhost:5555/uai/zerovp:b05
docker push localhost:5555/uai/zerovp:b05
curl http://localhost:5555/v2/_catalog
# {"repositories":["uai/zerovp"]}
```

### 5. What survives a restart

```bash
docker compose down
docker compose up -d
```

**The registry remembers its data:**

```bash
curl http://localhost:5555/v2/_catalog
# {"repositories":["uai/zerovp"]}
```

**The application does not:**

```bash
curl http://localhost:8080/api/info
# "todoCount": 4        (back to the four sample todos)
```

The difference comes down to a single line in `compose.yaml`:

```yaml
volumes:
  - registry-data:/var/lib/registry
```

A **named volume** is Docker-managed storage that exists independently of the
container. The registry writes into it, so its data survives the container being
removed and recreated. Our API has no volume and keeps state in process memory,
which dies with it.

The volume is visible from outside too:

```bash
docker volume ls --filter name=zerovp-b05
# zerovp-b05_registry-data

docker volume inspect zerovp-b05_registry-data
```

It goes away only with this:

```bash
docker compose down --volumes     # or -v
```

### 6. Watching for changes

```bash
docker compose watch
```

It runs in the foreground and watches the paths listed under `develop.watch`.
Change anything in `src/UAI.ZeroVP.API/`, save, and watch the output:

```
Rebuilding service(s) ["api"] after changes were detected...
service(s) ["api"] successfully built
```

Compose rebuilt the image and swapped the running containers itself. You typed no
`down`, no `build` and no `up`.

The available actions:

| Action | What it does on a change |
|---|---|
| `rebuild` | rebuilds the image and replaces the container - required for compiled languages such as C# |
| `sync` | only copies the changed files into the running container |
| `sync+restart` | copies the files and restarts the process |

For .NET the correct one is `rebuild`: the source has to be compiled. `sync` makes
sense for interpreted languages or static assets.

Stop it with `Ctrl+C`. The environment keeps running.

### 7. Cleaning up

```bash
docker compose down --volumes
```

Removes the containers, the network **and the named volume** - the registry contents
included. Without `--volumes` the volume would linger and take up space.

---

## Command summary

| Command | What it does |
|---|---|
| `docker compose config` | prints the resulting file; `--quiet` only checks the syntax |
| `docker compose up -d --build` | rebuilds and starts everything in the background |
| `docker compose ps` | service status |
| `docker compose logs -f api` | follow one service's logs |
| `docker compose exec api sh` | shell in a running container |
| `docker compose run --rm --entrypoint dotnet api --info` | one-off container outside the environment |
| `docker compose build --no-cache api` | rebuild one service without the cache |
| `docker compose restart api` | restart one service |
| `docker compose watch` | watch files and react to changes |
| `docker compose down` | stop and remove containers and the network |
| `docker compose down --volumes` | also remove named volumes |

A distinction people get wrong: **`exec` enters a running container**, whereas
**`run` creates a new throwaway one**. For diagnosis you want `exec`; for running a
helper command, `run --rm`.

Note the `--entrypoint` in that `run` row. Without it the arguments are merely appended
to the image's `ENTRYPOINT` and the API starts instead of `dotnet --info` - exactly the
trap from [example B1](../01-lifecycle/).

---

## Takeaways

- `docker compose config` is a cheap check before you start anything.
- `depends_on` with the `service_healthy` condition is almost always what you
  actually want. Plain `depends_on` handles start order only, not readiness.
- A named volume separates **data** from the **container**. What lives in process
  memory is gone; what lives in a volume survives.
- `docker compose watch` replaces the manual build - down - up cycle.
- `down` without `--volumes` leaves volumes behind. Remember that when cleaning up.
