# B3 - Layers, cache and disk space

## Goal

See an image as a stack of layers, be able to tell which one costs what, and stay
in control of how much disk space Docker eats.

## Assignment

1. Break the image `zerovp:b01` down into layers and find the three most expensive ones.
2. Find out how much space **the application itself** takes compared to what the
   base image brought along.
3. Find out how much space Docker occupies in total and how much of it can be reclaimed.
4. Try `Dockerfile.cache-mount` and **measure** whether it gained anything.
5. Decide when `--no-cache` is worth using and when it hurts.

---

## Solution

### 1. Breaking it into layers

```bash
docker image history zerovp:b01
```

More readably, just the essentials:

```bash
docker image history zerovp:b01 --format "{{.Size}}\t{{.CreatedBy}}" --no-trunc
```

```
0B        ENTRYPOINT ["dotnet" "UAI.ZeroVP.API.dll"]
0B        USER 1654
0B        HEALTHCHECK ...
0B        EXPOSE [8080/tcp]
0B        ENV ASPNETCORE_ENVIRONMENT=Production
1.81MB    COPY /app .                                    <-- our application
8.19kB    WORKDIR /app
6.49MB    RUN apt-get install curl ...                   <-- the health check
0B        LABEL ...
27.4MB    COPY /dotnet /usr/share/dotnet                 <-- ASP.NET Core
83.1MB    COPY /dotnet /usr/share/dotnet                 <-- the .NET runtime
...
```

Two observations:

- **Instructions such as `ENV`, `LABEL`, `EXPOSE`, `USER` and `ENTRYPOINT` cost 0 B.**
  They change metadata, not files. Do not hesitate to use them.
- Layers read bottom-up: the oldest is at the bottom.

### 2. How much the application weighs

**1.81 MB.** The rest of the 352 MB is the base image - the .NET runtime, ASP.NET
Core and the Ubuntu underneath.

There is a practical consequence. If you run ten applications on one machine, all
built on the same base image, **they do not take 10 x 352 MB**. The base image layers
are shared and stored on disk once. Ten applications therefore cost roughly 340 MB
plus 10 x a few megabytes.

You can confirm it on the totals:

```bash
docker system df
```

```
TYPE            TOTAL     ACTIVE    SIZE      RECLAIMABLE
Images          29        8         72.67GB   39.65GB (54%)
Containers      8         1         200MB     200MB (99%)
Local Volumes   28        5         2.016GB   1.591GB (78%)
Build Cache     292       0         20.63GB   20.63GB
```

The sum of the `SIZE` column in `docker images` is usually much larger than the
space actually used, precisely because shared layers get counted repeatedly.

### 3. Reclaiming space

A detailed breakdown:

```bash
docker system df -v
```

Work from least to most destructive:

```bash
docker container prune      # stopped containers
docker image prune          # untagged ("dangling") images left over from rebuilds
docker builder prune        # the build cache
```

> **Two commands delete more than you expect:**
>
> - `docker image prune -a` removes **every** image with no running container -
>   including ones you spent a long time downloading.
> - `docker system prune -a --volumes` additionally removes **volumes**, and with
>   them the data of databases and anything else you stored in them. This cannot
>   be undone.
>
> In a lab, run them only on your own machine and knowing what is on it.

The safest targeted cleanup after this course:

```bash
docker rm -f $(docker ps -aq --filter "name=zerovp") 2>/dev/null
docker rmi $(docker images -q "zerovp") 2>/dev/null
```

### 4. A persistent package cache - and why it did not help here

`Dockerfile.cache-mount` uses a technique worth knowing:

```dockerfile
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore UAI.ZeroVP.sln
```

A mount of type `cache` **is not an image layer**. Docker keeps it aside, it never
reaches the resulting image, and it survives `docker build --no-cache`.

Measured on this project (with `--no-cache`, so the restore always runs):

| Variant | 1st run | 2nd run |
|---|---|---|
| Without `--mount=type=cache` | 7 s | 7 s |
| With `--mount=type=cache` | 7 s | 6 s |

**The difference is within measurement error.** The reason is simple: our project
has two small NuGet dependencies. There is nothing to cache.

That is a more valuable lesson than the technique itself: **measure first, optimise
second.** Taking a "proven" Dockerfile full of tricks off the internet and never
checking whether any of them helps your project is a common mistake.

Where `--mount=type=cache` genuinely pays off:

- projects with dozens or hundreds of NuGet packages
- `npm ci` on front ends (`target=/root/.npm`)
- compiling native code (`ccache`)
- build servers, where the build cache is discarded between runs

### 5. When to use `--no-cache`

```bash
docker build --no-cache -f ... -t ... .
```

**Use it when:**

- verifying the build also works on a clean machine (typically before handing it
  to colleagues)
- you need to re-fetch something that may have changed behind the same address -
  `apt-get update` or a `git clone` of a branch, for instance
- debugging odd behaviour you suspect comes from a stale layer

**Do not use it for everyday work.** Seven seconds here, half a minute on a larger
project, several times an hour - it adds up.

A middle ground, when you want to invalidate only part of it:

```bash
# invalidates layers from the named stage onwards
docker build --no-cache-filter build -f ... .
```

### The cache on a build server

On a build server the cache is gone after every run. The answer is to store it in
a registry:

```bash
docker build \
    --cache-to   type=registry,ref=myregistry/zerovp:cache,mode=max \
    --cache-from type=registry,ref=myregistry/zerovp:cache \
    -t myregistry/zerovp:1.4.2 .
```

Registries are covered in [example 4](../04-tags-and-registry/).

---

## Takeaways

- `docker image history` shows which instruction cost how much space. Use it before
  you start optimising a Dockerfile.
- Metadata (`ENV`, `LABEL`, `USER`, `EXPOSE`) costs nothing.
- Shared base image layers are stored on disk only once.
- `docker system df` tells you how much can be reclaimed; `prune` with `-a` or
  `--volumes` also removes things you want to keep.
- Optimising without measuring is guessing. Measure the cases where nothing helped, too.
