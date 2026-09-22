# A3 - Hardening the image

## Goal

Turn a working image into one you are not embarrassed to let off your own machine.
Three changes: an unprivileged account, a health check, descriptive metadata.

## Assignment

1. Build `zerovp:a03` and compare its size with `zerovp:a02`. Work out where the
   difference comes from.
2. Run a container and watch the `STATUS` column in `docker ps` go from
   `(health: starting)` to `(healthy)`.
3. Find out which user the application runs as inside the container.
4. Try to create a file in `/app` inside the container. What happens, and why?
5. Inspect the image metadata - find the `LABEL` entries and the `HEALTHCHECK` setup.
6. **Think about it:** why must the `USER` line come after `RUN apt-get install`?

Expected result: `docker ps` shows `(healthy)`, `id` inside the container returns
`uid=1654(app)`.

---

## Solution

### 1. Size

```
zerovp:a02   343MB
zerovp:a03   352MB
```

Nine megabytes more, all of it the installed `curl`. The base image `aspnet:10.0`
**ships with neither curl nor wget** - it is deliberately trimmed. Without one of
them `HEALTHCHECK` would have nothing to call `/health` with.

It is a conscious trade: 9 MB so that Docker can tell on its own when the
application stops responding. In production that pays for itself.

> If you want the image without that trade, reach for the
> `aspnet:10.0-noble-chiseled` variant. It has no shell and no package manager at
> all, so curl cannot be installed and health checking has to be arranged from
> outside (in Compose or in an orchestrator).

### 2. The health check

```bash
docker build -f case-study-a-dockerfile/03-hardening/Dockerfile -t zerovp:a03 .
docker run -d --name zerovp-a03 -p 8080:8080 zerovp:a03
docker ps
```

Right after startup:

```
NAMES         STATUS
zerovp-a03    Up 2 seconds (health: starting)
```

Once `--start-period` has elapsed:

```
NAMES         STATUS
zerovp-a03    Up 25 seconds (healthy)
```

The status can also be read on its own, which is handy in scripts:

```bash
docker inspect --format "{{.State.Health.Status}}" zerovp-a03
```

And the history of recent checks, including their output:

```bash
docker inspect --format "{{range .State.Health.Log}}{{.ExitCode}} {{.Output}}{{end}}" zerovp-a03
```

Try the opposite state as well. The `--health-cmd` switch overrides the check at
run time - we point it at a port where nothing is listening:

```bash
docker run -d --name zerovp-sick \
    --health-cmd="curl --fail --silent http://localhost:9999/health" \
    --health-interval=3s --health-retries=2 --health-start-period=2s \
    zerovp:a03

docker ps --filter name=zerovp-sick
# zerovp-sick   Up 8 seconds (unhealthy)

docker rm -f zerovp-sick
```

**Watch out for a common misconception:** Docker does nothing about an unhealthy
container - it leaves it running and merely labels it. Not even `--restart` will
restart it; that policy reacts only to the process exiting. For health status to
have any effect, something has to read it: Compose via `depends_on` with the
`service_healthy` condition (example 5), or an orchestrator such as Kubernetes.
`HEALTHCHECK` on its own is diagnostics.

### 3. Which user

```bash
docker exec zerovp-a03 id
```

```
uid=1654(app) gid=1654(app) groups=1654(app)
```

Compare with the image from example 2:

```bash
docker run -d --name zerovp-a02 zerovp:a02 && docker exec zerovp-a02 id
# uid=0(root) gid=0(root) groups=0(root)
docker rm -f zerovp-a02
```

**Why it matters.** Root inside a container is not the same as root on the host,
but it is still a needless risk. If an attacker exploited a bug in the application,
they would hold unlimited rights inside the container - they could install tools,
alter system files and look for a way out. An unprivileged account makes that harder.

The `app` user with UID 1654 is created by Microsoft in its own base images, and
its number is published in the `APP_UID` variable. That is why the single line
`USER $APP_UID` is enough.

### 4. Writing to /app

```bash
docker exec zerovp-a03 touch /app/probe
# touch: cannot touch '/app/probe': Permission denied
```

The application files are owned by root, because the build stage copied them there.
The `app` user may read and execute them but not modify them. This is the **desired
state**: a running application has no business rewriting its own binaries, and an
attacker loses a convenient way to smuggle something into the container.

If the application did need to write somewhere, it would get a dedicated place -
`--tmpfs /tmp` or a mounted volume, for instance.

### 5. Image metadata

```bash
docker inspect zerovp:a03 --format "{{json .Config.Labels}}"
docker inspect zerovp:a03 --format "{{json .Config.Healthcheck}}"
docker image history zerovp:a03
```

`docker image history` lists the individual layers with their sizes - a neat way
to see which instruction cost how much.

### 6. Why `USER` comes after `apt-get`

Installing packages writes into system directories, which requires root. If
`USER $APP_UID` sat above `RUN apt-get install`, the build would fail with
`Permission denied`.

The general rule: **put `USER` as low as possible**, right before `ENTRYPOINT`.
Everything that needs elevated rights must be above it.

### Cleaning up

```bash
docker rm -f zerovp-a03
```

---

## Takeaways

| Instruction | What it is for |
|---|---|
| `LABEL` | descriptive image metadata (author, licence, description) |
| `ENV` | environment variable inside the container, overridable with `-e` |
| `HEALTHCHECK` | Docker periodically verifies the application responds |
| `USER` | the account the application runs as - put it as low as possible |

- Install nothing you do not need. Every package is extra size and a potential
  vulnerability.
- `apt-get update`, `install` and the cleanup belong in **one** `RUN`. Otherwise the
  package index stays in the layer below and the image grows for nothing.
