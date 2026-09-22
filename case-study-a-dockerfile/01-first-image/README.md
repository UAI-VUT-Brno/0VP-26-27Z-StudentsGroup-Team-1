# A1 - First image

## Goal

Get an existing .NET application into a container by the shortest route, and
understand the difference between an **image** and a **container**.

- An **image** is an immutable template - the compiled application plus the
  operating system around it.
- A **container** is a running instance of that template. One image can produce
  as many as you like.

The analogy: the image is the class, the container is the instance.

## Assignment

1. Read the `Dockerfile` in this folder and try to predict what each line does.
2. Build an image named `zerovp:a01`.
3. Run a container from it so the application is reachable at `http://localhost:8080`.
4. Verify the API answers - call `/health` and `/api/todos`.
5. Find out how large the image turned out. Think about why it is that large.
6. Stop and remove the container.

Expected result: the API responds, `docker images` shows an image over 1 GB.

---

## Solution

### 1. Building the image

```bash
# run this from the repository root K:\DockerTrivia
docker build -f case-study-a-dockerfile/01-first-image/Dockerfile -t zerovp:a01 .
```

- `-f` says where the Dockerfile is.
- `-t zerovp:a01` is the name and tag of the resulting image. Without a tag it
  gets `latest`.
- The trailing dot is the **build context** - the directory whose contents are
  sent to Docker. Without it `COPY` would have nothing to copy.

### 2. Running a container

```bash
docker run -d --name zerovp-a01 -p 8080:8080 zerovp:a01
```

- `-d` (detached) runs it in the background and returns you to the prompt.
- `--name` gives the container a name so you can address it later.
- `-p 8080:8080` maps ports as `host_port:container_port`.
  **This is what actually opens the port** - `EXPOSE` in the Dockerfile is only
  documentation.

### 3. Verification

```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/todos
curl http://localhost:8080/api/info
```

`/api/info` returns something like:

```json
{"machineName":"533a283ffd80","environment":"Production","version":"1.0.0", ...}
```

Two things worth noticing:

- `machineName` is not your computer's name but the **short container ID**.
  The application knows nothing about its surroundings - it sees only its container.
- `environment` is `Production`, even though `dotnet run` showed `Development`.
  That is because the environment was set by `launchSettings.json`, a file used
  only by `dotnet run` on a developer machine. It does not apply in a container -
  which is also why Swagger is not available here. Case study C switches it on.

### 4. Image size

```bash
docker images zerovp:a01
```

```
REPOSITORY   TAG   SIZE
zerovp       a01   1.34GB
```

**Why so large?** Because the base image `mcr.microsoft.com/dotnet/sdk:10.0`
(1.3 GB on its own) carries the complete developer SDK: compiler, debugging tools,
project templates. None of that is needed to *run* a finished application.
It is like shipping your customer the entire development environment.

On top of that, the `bin/` and `obj/` folders from your disk ended up in the image -
compiled for Windows, therefore useless inside a Linux container.

Example 2 fixes both.

### 5. Cleaning up

```bash
docker stop zerovp-a01     # signals the process to terminate
docker rm zerovp-a01       # removes the container (the image stays)
```

Shortcut for both at once: `docker rm -f zerovp-a01`.

---

## Takeaways

| Term | Meaning |
|---|---|
| `FROM` | the base image we build on |
| `WORKDIR` | working directory inside the image for the following instructions |
| `COPY` | moves files from the build context into the image |
| `RUN` | executes a command **during the build** and stores the result as a layer |
| `EXPOSE` | documentation only, does not open a port |
| `ENTRYPOINT` | the command executed **when the container starts** |
| `-p 8080:8080` | port mapping host : container, this is what really opens it |

The key distinction: `RUN` happens once at build time, `ENTRYPOINT` on every
container start.
