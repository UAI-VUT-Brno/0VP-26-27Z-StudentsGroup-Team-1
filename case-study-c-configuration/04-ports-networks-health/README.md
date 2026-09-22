# C4 - Ports, networks and health

## Goal

Take control of **how traffic reaches the container** and how you know things inside
are fine. Plus two measures that harden a container to a production-usable level.

## Assignment

1. Change the application's internal port to 5000 and expose it on 9090.
   Watch what that does to the health status and explain why.
2. Make the container reachable **only from this computer**, not from the whole network.
3. Override the health check at run time without changing the image.
4. Run the container with a **read-only filesystem** and verify the application works.
5. Find out how much memory the container may use, and cap it.
6. Attach the container to two networks and think about what that is good for.

---

## Solution

### 1. A different internal port

```bash
docker run -d --name cfg \
    -e ASPNETCORE_HTTP_PORTS=5000 \
    -p 9090:5000 \
    zerovp:c01

curl http://localhost:9090/health
# Healthy
```

It works. But look at the status after a while:

```bash
docker ps --filter name=cfg
# NAMES   STATUS
# cfg     Up 54 seconds (unhealthy)

docker rm -f cfg
```

**The health check is baked into the image** and calls `http://localhost:8080/health`
literally:

```dockerfile
HEALTHCHECK ... CMD curl --fail --silent http://localhost:8080/health || exit 1
```

You changed the application's port, but the check does not know. It is the same
mistake as in [example B2](../../case-study-b-tooling/02-troubleshooting/) - only here you
caused it from the command line.

The lesson: **what can be configured from outside must be configurable from outside
in full.** If you let the port be changed, the health check has to cope with it.
The fix is in step 3.

### 2. Binding to this machine only

```bash
docker run -d --name cfg -p 127.0.0.1:8080:8080 zerovp:c01
docker port cfg
# 8080/tcp -> 127.0.0.1:8080

docker rm -f cfg
```

Compare with the usual `-p 8080:8080`:

```
8080/tcp -> 0.0.0.0:8080        <-- reachable from ALL network interfaces
8080/tcp -> [::]:8080
```

**`0.0.0.0` means "from anywhere".** On a laptop in a campus network that means
everyone on that network can see your application. For databases and admin
interfaces it tends to be a serious problem - and Docker additionally bypasses host
firewall rules in some configurations.

The rule: **if it does not need to be outside, bind it to `127.0.0.1`.**

### 3. Overriding the health check at run time

```bash
docker run -d --name cfg \
    -e ASPNETCORE_HTTP_PORTS=5000 \
    -p 9090:5000 \
    --health-cmd="curl --fail --silent http://localhost:5000/health || exit 1" \
    --health-interval=15s \
    --health-start-period=20s \
    --health-retries=3 \
    zerovp:c01

sleep 25 && docker ps --filter name=cfg
# cfg   Up 25 seconds (healthy)
```

Status details - while that container is still running:

```bash
docker inspect cfg --format "{{json .State.Health}}"
docker inspect cfg --format "{{range .State.Health.Log}}{{.Start}} code={{.ExitCode}} {{.Output}}{{end}}"

docker rm -f cfg
```

Disabling the check entirely (for instance when an orchestrator handles it):

```bash
docker run -d --name cfg --no-healthcheck -p 8080:8080 zerovp:c01

docker inspect cfg --format "{{json .State.Health}}"
# null        <-- there is no health state at all now

docker rm -f cfg
```

Keep that order in mind: with `--no-healthcheck` the `.State.Health` object does not
exist, so the two commands above have nothing to read and the `range` one errors out.
`docker ps` then shows a plain `Up 10 seconds` with nothing in brackets - which is also
what a container built from a Dockerfile without `HEALTHCHECK` looks like.

### 4. A read-only filesystem

```bash
docker run -d --name cfg --read-only --tmpfs /tmp -p 8080:8080 zerovp:c01

curl http://localhost:8080/health
# Healthy      <-- the application works

docker exec cfg touch /app/x
# touch: cannot touch '/app/x': Read-only file system

docker exec cfg touch /tmp/x
# succeeds - /tmp is a tmpfs, i.e. a write into memory
docker rm -f cfg
```

`--read-only` blocks writes anywhere in the container. `--tmpfs /tmp` then gives
back a writable `/tmp` that lives **in memory** and dies with the container.

Measured: our application also works with `--read-only` and no `--tmpfs` at all.
Even so, leaving `/tmp` writable is recommended - .NET creates diagnostic sockets
there (you saw them in `docker diff` in example A4) and plenty of libraries assume
they can write to `/tmp`.

The combination `--read-only` + `--tmpfs /tmp` + `USER` (the unprivileged account
from the Dockerfile) is a very solid base. An attacker who gets inside can neither
store nor overwrite anything.

### 5. Resource limits

With no limit, a container may consume all the memory on the machine:

```bash
docker run -d --name cfg -p 8080:8080 zerovp:c01

docker stats --no-stream cfg
# NAME   CPU %   MEM USAGE / LIMIT
# cfg    0.04%   30.14MiB / 31.04GiB      <-- the limit is the whole machine

docker rm -f cfg
```

With a limit:

```bash
docker run -d --name cfg -p 8080:8080 \
    --memory=256m \
    --cpus=0.5 \
    zerovp:c01

docker stats --no-stream cfg
# cfg    0.03%   23.22MiB / 256MiB
docker rm -f cfg
```

A measured side benefit: with no limit the application used 30.1 MiB, with a 256 MB
limit only 23.2 MiB. The .NET runtime reads the container limit and tunes garbage
collection to it. Without a limit it goes by the size of the whole machine, which on
a shared server means the application hogs far more than it needs.

When a container exceeds its memory limit the kernel kills it. You recognise it thus:

```bash
docker inspect <name> --format "{{.State.OOMKilled}} {{.State.ExitCode}}"
# true 137
```

### 6. Two networks

```bash
docker network create public
docker network create internal

docker run -d --name cfg --network public -p 8080:8080 zerovp:c01
docker network connect internal cfg

docker inspect cfg --format "{{range \$k,\$v := .NetworkSettings.Networks}}{{\$k}} -> {{\$v.IPAddress}}{{println}}{{end}}"
# public   -> 172.23.0.2
# internal -> 172.24.0.2
```

The container has a different address on each network. The typical use: `public`
for user traffic, `internal` for talking to a database that has no business being on
the public network. The database then has **no** outward port and is reachable only
from the containers sharing its internal network.

In Compose the same thing reads more clearly:

```yaml
services:
  api:
    networks: [public, internal]
  database:
    networks: [internal]        # no "ports" - unreachable from outside
```

### Cleaning up

```bash
docker rm -f cfg
docker network rm public internal
```

---

## Takeaways

| Switch | What it does |
|---|---|
| `-p 127.0.0.1:8080:8080` | exposes it on this machine only |
| `--health-cmd`, `--health-interval` | overrides the health check from the image |
| `--no-healthcheck` | disables the health check |
| `--read-only` | read-only filesystem |
| `--tmpfs /tmp` | a writable folder in memory |
| `--memory`, `--cpus` | resource limits |
| `docker network connect` | attaches the container to another network |

- `-p 8080:8080` exposes the application to the whole network. Often that is not
  what you want.
- A health check baked into the image has to match the way you run the container.
- `--read-only` is cheap and effective hardening. Try it on any application that
  does not need to write.
- The .NET runtime reads the memory limit and tunes garbage collection to it. Set it.
