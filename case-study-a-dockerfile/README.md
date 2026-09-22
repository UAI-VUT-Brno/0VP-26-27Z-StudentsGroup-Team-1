# Case study A - Building up the Dockerfile

**Level: beginner.** Assumes this is your first encounter with Docker.

Five examples, each improving the same Dockerfile. Every step has a measurable
payoff - a smaller image, a faster build, a safer runtime - so it is always clear
why the change is worth making.

| # | Example | What it adds | Result |
|---|---|---|---|
| 1 | [First image](01-first-image/) | `FROM`, `COPY`, `RUN`, `ENTRYPOINT` | the app runs in a container, image 1.34 GB |
| 2 | [Multi-stage build](02-multi-stage-build/) | second stage, ignore file, layer order | image 343 MB, faster rebuilds |
| 3 | [Hardening](03-hardening/) | `USER`, `HEALTHCHECK`, `ENV`, `LABEL` | unprivileged runtime, supervised health |
| 4 | [Networks and diagnostics](04-networks-and-diagnostics/) | `docker network`, `logs`, `exec`, `stats` | two containers talking, and you can look inside |
| 5 | [Docker Compose](05-compose/) | `compose.yaml`, nginx, replicas | the whole environment in one command |

## Before you start

Check that Docker is running with Linux containers:

```bash
docker version
docker info --format "{{.OSType}}"   # must print: linux
```

## Build context - read this, it will save you trouble

Every example builds **from the repository root** `K:\DockerTrivia`, because that
is where the application source lives. The Dockerfile itself sits in the example
folder, so it has to be pointed at with `-f`:

```bash
# the trailing dot = build context (what gets sent to Docker)
# -f = where the Dockerfile lives
docker build -f case-study-a-dockerfile/01-first-image/Dockerfile -t zerovp:a01 .
```

The most common mistake is running `docker build` from inside the example folder.
The context is then that folder, Docker finds no source code in it, and the build
fails on `COPY`.

## Cleaning up after the lab

```bash
docker rm -f $(docker ps -aq --filter "name=zerovp")   # containers
docker rmi $(docker images -q "zerovp")                # images
docker system df                                       # how much space Docker uses
```
