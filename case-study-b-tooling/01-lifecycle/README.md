# B1 - Image and container lifecycle

## Goal

Walk the whole path from source code to a running container and back to a tidy
system. Master the commands you will type every day.

## Assignment

1. Build the image `zerovp:b01`, passing it version `1.4.2` at build time.
2. Verify the version reached both the API and the image labels.
3. Run a container in the background, verify it works, then stop it -
   but **do not remove it**. Confirm it still exists.
4. Start it again without creating a new one.
5. Run a second container from the same image on a different port.
6. Find the exit code of a stopped container.
7. Run a container that removes itself when it finishes.
8. Clean up - containers and images.

---

## Solution

### 1. Building with a version

```bash
docker build -f case-study-b-tooling/01-lifecycle/Dockerfile \
             --build-arg APP_VERSION=1.4.2 \
             -t zerovp:b01 .
```

`ARG` in a Dockerfile is a variable that exists **only during the build**. That
distinguishes it from `ENV`, which stays in the finished image and is visible at
run time. `ARG` suits things like a version number or a commit hash.

### 2. Where the version ended up

```bash
docker run -d --name b01 -p 8080:8080 zerovp:b01
curl http://localhost:8080/api/info
```

```json
{"machineName":"...","version":"1.4.2", ...}
```

It reached the build through `-p:Version=${APP_VERSION}` in `dotnet publish`.
And it reached the image metadata too:

```bash
docker inspect zerovp:b01 --format '{{index .Config.Labels "org.opencontainers.image.version"}}'
# 1.4.2
```

This way you can find out which application version an image holds without ever
running it.

### 3. Stopping without removing

```bash
docker stop b01
docker ps            # b01 is not here
docker ps -a         # but here it is, in state Exited
```

```
NAMES   STATUS
b01     Exited (0) 5 seconds ago
```

`docker ps` lists only running containers, `docker ps -a` lists all of them. A
stopped container still occupies disk space and holds on to its name - which is
why a second container cannot use the same `--name`.

### 4. Starting it again

```bash
docker start b01
docker ps
```

`start` revives **the same** container, including its write layer. Our API keeps
data in process memory, though, so any todos created before the stop are gone.

A distinction worth memorising:

| Command | What it does |
|---|---|
| `docker run` | creates a **new** container from an image and starts it |
| `docker start` | starts an **existing** stopped container |
| `docker restart` | stops and starts an existing container |

### 5. A second container on another port

```bash
docker run -d --name b01-second -p 8081:8080 zerovp:b01
docker ps --format "table {{.Names}}\t{{.Ports}}\t{{.Status}}"
```

```
NAMES         PORTS                    STATUS
b01-second    0.0.0.0:8081->8080/tcp   Up 3 seconds (healthy)
b01           0.0.0.0:8080->8080/tcp   Up 2 minutes (healthy)
```

Inside both containers the application listens on 8080. They differ only in which
host port we map them to. Two containers cannot share one host port:

```bash
docker run -d --name b01-third -p 8080:8080 zerovp:b01
# docker: Error response from daemon: ... driver failed programming external
# connectivity on endpoint b01-third:
# Bind for 0.0.0.0:8080 failed: port is already allocated
```

### 6. Exit code

```bash
docker stop b01-second
docker inspect b01-second --format "{{.State.ExitCode}} {{.State.Status}}"
# 0 exited
```

Code `0` means a clean shutdown. When an application crashes it will be something
else - and it is the first thing to look at on a broken container.

Try the hard kill too:

```bash
docker start b01-second && docker kill b01-second
docker inspect b01-second --format "{{.State.ExitCode}}"
# 137   (128 + 9, i.e. SIGKILL)
```

`docker stop` sends `SIGTERM` and gives the process 10 seconds to finish its work.
`docker kill` cuts it off immediately with `SIGKILL`. In production always use `stop`.

### 7. A self-cleaning container

```bash
docker run --rm -p 8082:8080 zerovp:b01
```

Without `-d` it runs in the foreground, its output goes straight to the screen and
`Ctrl+C` ends it. Thanks to `--rm` it then removes itself. Ideal for a quick trial,
not for production.

Running a different command instead of the application is less straightforward than
it looks. Try this first and watch what happens:

```bash
docker run --rm zerovp:b01 dotnet --list-runtimes
```

Instead of listing runtimes the API starts up and keeps running until you press
`Ctrl+C`. The reason: everything after the image name replaces `CMD`, but our
Dockerfile uses `ENTRYPOINT`, and `CMD` is merely **appended to it as arguments**.
What actually ran was:

```
dotnet UAI.ZeroVP.API.dll dotnet --list-runtimes
```

An ASP.NET Core application silently ignores unknown arguments, so it simply started.

Genuinely replacing it requires `--entrypoint`:

```bash
docker run --rm --entrypoint dotnet zerovp:b01 --list-runtimes
# Microsoft.AspNetCore.App 10.0.12 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]
# Microsoft.NETCore.App 10.0.12 [/usr/share/dotnet/shared/Microsoft.NETCore.App]

docker run --rm --entrypoint sh zerovp:b01 -c "ls -la /app | head"
```

Remember the distinction: **`ENTRYPOINT` is what runs, `CMD` is its default
arguments.** Arguments after the image name override `CMD`, not `ENTRYPOINT`.

### 8. Cleaning up

```bash
docker rm -f b01 b01-second         # -f stops running ones too
docker rmi zerovp:b01               # removes the image
docker container prune              # removes ALL stopped containers
```

`docker rmi` fails while any container exists from that image, even a stopped one:

```
Error response from daemon: conflict: unable to delete zerovp:b01 (must be forced)
- container 3e600c867301 is using its referenced image 2e169b42e06a
```

The culprit is `b01-third` from step 5. Its port mapping failed, but the container
itself **was created** - `docker ps -a` still lists it as `Created`, and that alone is
enough to block `docker rmi`. A failed `docker run` leaves a container behind.

So: containers first, image second. `docker rmi -f` does go through, but it only
drops the tag - the layers stay alive as long as a container uses them.

---

## Takeaways

| Command | What for |
|---|---|
| `docker build -t name:tag .` | builds an image |
| `docker build --build-arg KEY=value` | passes a value into `ARG` |
| `docker run -d --name x -p 8080:8080 img` | new container in the background |
| `docker run --rm img` | container that removes itself when done |
| `docker ps` / `docker ps -a` | running / all containers |
| `docker start` / `stop` / `restart` / `kill` | acting on an existing container |
| `docker rm` / `docker rmi` | remove a container / an image |
| `docker container prune` | clear out all stopped containers |

- `run` creates a new container, `start` revives an existing one. Confusing the two
  is the single most common source of beginner bewilderment.
- `stop` is a polite shutdown, `kill` is a cut-off. Exit code `137` tells you which.
