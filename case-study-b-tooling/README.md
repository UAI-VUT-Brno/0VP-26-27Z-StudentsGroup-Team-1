# Case study B - Tooling and workflow

**Level: intermediate.** Assumes you have run `docker run` before and know what an
image and a container are. If not, start with [case study A](../case-study-a-dockerfile/).

The Dockerfile barely changes here. The weight is elsewhere: on what you can do
*with* images and containers - inspect them, debug them, version them, ship them.

| # | Example | What it covers |
|---|---|---|
| 1 | [Lifecycle](01-lifecycle/) | `build`, `run`, `ps`, `stop`, `rm`, `rmi`, exit codes |
| 2 | [Troubleshooting](02-troubleshooting/) | two broken Dockerfiles to fix, `logs`, `exec`, `inspect` |
| 3 | [Layers and cache](03-layers-and-cache/) | `history`, cache, `--no-cache`, `system df`, `prune` |
| 4 | [Tags and registry](04-tags-and-registry/) | tagging, a private registry, `push`, `pull`, `save`, `load` |
| 5 | [Docker Compose](05-compose/) | the whole environment in one command, `watch`, `config` |

## Build context

Every example builds **from the repository root** `K:\DockerTrivia`:

```bash
docker build -f case-study-b-tooling/01-lifecycle/Dockerfile -t zerovp:b01 .
```

## The shared Dockerfile

Examples 2 to 5 use the Dockerfile from example 1: a multi-stage build with an
unprivileged account and a health check - the end result of case study A. The exception
is example 2, which has its own **deliberately broken** files.

## Cleaning up after the lab

```bash
docker rm -f $(docker ps -aq --filter "name=zerovp") 2>/dev/null
docker rmi $(docker images -q "zerovp") 2>/dev/null
docker system df
```
