# B2 - Troubleshooting

## Goal

Learn to find the cause when something does not work. This folder holds **two
deliberately broken Dockerfiles**. Each fails differently and each is uncovered
with different tools.

A procedure that almost always works:

1. **Build failed?** Read the last lines of the output - Docker names the instruction.
2. **Container did not start?** `docker ps -a` and the exit code, then `docker logs`.
3. **Container runs but does not answer?** `docker logs`, then `docker exec` and try
   it from the inside.

## Assignment

### Task 1 - `Dockerfile.build-failure`

```bash
docker build -f case-study-b-tooling/02-troubleshooting/Dockerfile.build-failure -t zerovp:broken1 .
```

1. The build fails. Find in the message which instruction caused it.
2. Explain the cause and fix the file.

### Task 2 - `Dockerfile.runtime-failure`

```bash
docker build -f case-study-b-tooling/02-troubleshooting/Dockerfile.runtime-failure -t zerovp:broken2 .
docker run -d --name broken -p 8080:8080 zerovp:broken2
curl http://localhost:8080/health
```

3. The build succeeds and the container runs, but `curl` gets no answer. Find out why.
   Use `docker logs`, `docker port` and `docker exec`.
4. Wait about a minute and look at the `STATUS` column in `docker ps`.
5. Fix the Dockerfile in **two different ways** and compare them.

---

## Solution

### Task 1 - build failure

The output ends like this:

```
 > [runtime 3/3] COPY --from=build /publish .:
--------------------
  21 |     WORKDIR /app
  22 | >>> COPY --from=build /publish .
  23 |     EXPOSE 8080
--------------------
ERROR: failed to build: failed to solve: failed to compute cache key:
failed to calculate checksum of ref ...: "/publish": not found
```

Docker tells you everything that matters: **which line** (`>>>` marks the culprit)
and **what it could not find** (`/publish`).

The cause: the `build` stage publishes the application into `/app`, but the
`runtime` stage looks for it in `/publish`. That folder was never created.

The fix:

```dockerfile
COPY --from=build /app .
```

> The usual variants of this mistake: a typo in the path, copying from an unnamed
> stage, or a path excluded by the ignore file. Always check that the source path
> in `COPY` really exists in the stage you are copying from.

### Task 2 - runtime failure

This one is nastier, because the build succeeds and `docker ps` looks fine:

```bash
docker ps
# NAMES     STATUS                            PORTS
# broken    Up 5 seconds (health: starting)   0.0.0.0:8080->8080/tcp
```

But nothing answers from outside:

```bash
curl --max-time 5 http://localhost:8080/health
# curl: (56) Recv failure / empty reply
```

#### Step 1: the logs

```bash
docker logs broken
```

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:5000        <-- there it is
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
```

The application listens on port **5000**, not 8080.

#### Step 2: confirm from the inside

```bash
docker exec broken curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/health
# 200        <-- the application works

docker exec broken curl -s --max-time 3 -o /dev/null -w "%{http_code}\n" http://localhost:8080/health
# 000        <-- nothing is listening on 8080
```

#### Step 3: the cause

The Dockerfile says:

```dockerfile
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=5000       # <-- overrode the base image default of 8080
EXPOSE 8080                          # <-- the documentation claims otherwise
```

The `-p 8080:8080` mapping points at port 8080 in the container, where nobody is
listening. Docker does not complain - mapping a port nothing listens on is allowed.

**`EXPOSE` is documentation and Docker never checks it against reality.** That is
exactly why it is so easy for it to drift away from the truth.

#### Step 4: health status

After roughly 40 seconds (a 15 s start period plus three failures 10 s apart):

```bash
docker ps
# NAMES     STATUS
# broken    Up 38 seconds (unhealthy)
```

`HEALTHCHECK` calls `http://localhost:8080/health` and fails. Had the Dockerfile
carried no health check, the container would have looked perfectly fine from the
outside - a good argument for including one.

#### Step 5: two fixes

**Fix A - move the rest to match the application.** Keep the app on 5000 and align
everything else:

```dockerfile
ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000
HEALTHCHECK ... CMD curl --fail --silent http://localhost:5000/health || exit 1
```

Run it with `docker run -p 8080:5000 ...`.

**Fix B - go back to the default port.** Drop the `ASPNETCORE_HTTP_PORTS` line
entirely and rely on the base image value of 8080:

```dockerfile
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
```

**Fix B is the better one.** Microsoft's base images set
`ASPNETCORE_HTTP_PORTS=8080` precisely so that the port is the same across projects.
Changing it without a reason means building a trap for yourself and your colleagues.

---

## Troubleshooting cheat sheet

| Situation | First move |
|---|---|
| Build failed | last lines of the output - Docker marks the instruction with `>>>` |
| Container exited immediately | `docker ps -a`, then `docker logs <name>` |
| Container runs but does not answer | `docker logs`, then `docker exec` and try from inside |
| Unsure where the ports go | `docker port <name>` |
| Unsure what is configured inside | `docker exec <name> env \| sort` |
| Container is `unhealthy` | `docker inspect --format "{{json .State.Health}}" <name>` |
| Unsure what actually starts it | `docker inspect --format "{{.Config.Entrypoint}} {{.Config.Cmd}}"` |

Useful details:

```bash
# logs with timestamps, last 50 lines only, followed live
docker logs -f --tail 50 --timestamps broken

# logs from the last minute only
docker logs --since 1m broken

# every environment variable inside the container
docker exec broken env | sort

# what the process inside really is
docker top broken

# the full configuration, which tells you absolutely everything
docker inspect broken | less
```

### When the container exits immediately

That is the case where `docker exec` cannot help - there is nothing to enter.
What to do instead:

```bash
docker ps -a                                    # find it and read the exit code
docker logs <name>                              # the application's last words
docker run --rm --entrypoint sh -it <image>     # walk into the image by hand
```

The last command starts a shell instead of the application, so you can look around
inside the image and run the application manually to see the failure live.

### Cleaning up

```bash
docker rm -f broken
docker rmi zerovp:broken2
```

Only `broken2` exists. Task 1 is the build that fails **by design**, so `zerovp:broken1`
was never created and asking Docker to remove it just reports `No such image`.

---

## Takeaways

- Docker's error messages are usable - they name the line and the reason. Read them.
- `EXPOSE` checks nothing. The real port comes from `docker logs`, not the Dockerfile.
- The debugging order is **logs -> inside via exec -> inspect**. The first step is
  almost always enough.
- `HEALTHCHECK` has diagnostic value too: without it a broken container looks healthy.
