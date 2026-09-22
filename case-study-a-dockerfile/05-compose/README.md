# A5 - Docker Compose

## Goal

Replace long `docker run` commands with one file and one command. And above all:
demonstrate vividly what it means that **each replica has its own memory**.

Compose is a tool that describes a whole environment in a `compose.yaml` file and
then starts, stops or rebuilds all of it at once. It creates the networks for you.

## Assignment

1. Study `compose.yaml` and `nginx.conf`. Work out how many containers appear and
   which port leads into the environment.
2. Start the whole environment with a single command.
3. Call `/api/info` ten times in a row. Watch the `machineName` field.
4. Create a todo, then check the todo count several times. Explain the result.
5. Scale the API to five replicas **without stopping the environment** and verify
   that nginx picked the new replicas up by itself.
6. Look at the logs of all replicas at once.
7. Stop the environment and clean up.

Expected result: `machineName` alternates between three values; the todo count
differs between replicas.

---

## Solution

### 1. What is in the file

| Service | Image | Ports | Count |
|---|---|---|---|
| `api` | built from the example 3 Dockerfile | internal 8080 only | 3 replicas |
| `nginx` | `nginx:1.29-alpine` | `8080:80` outward | 1 |

One port leads outward - `8080` on the host, pointing at nginx's port 80.
The API replicas have no outward port and could not have one: three containers
cannot share the same host port.

### 2. Starting it

```bash
docker compose -f case-study-a-dockerfile/05-compose/compose.yaml up -d --build
```

- `up` creates and starts everything in the file
- `-d` in the background
- `--build` forces the image to be built (without it an existing one is reused)

```bash
docker compose -f case-study-a-dockerfile/05-compose/compose.yaml ps
```

```
NAME                  SERVICE   STATUS
zerovp-a05-api-1      api       Up 8 seconds (healthy)
zerovp-a05-api-2      api       Up 8 seconds (healthy)
zerovp-a05-api-3      api       Up 8 seconds (healthy)
zerovp-a05-nginx-1    nginx     Up 8 seconds
```

Container names follow `<project name>-<service>-<ordinal>`.
The `(healthy)` status comes from the `HEALTHCHECK` in example 3 - Compose
inherited it from the image.

> To avoid typing the path every time, switch into the example folder:
> ```bash
> cd case-study-a-dockerfile/05-compose
> docker compose up -d --build
> ```
> Paths inside `compose.yaml` are written relative to it, so this works.

### 3. Which replica answers

```bash
for i in $(seq 1 10); do curl -s http://localhost:8080/api/info; echo; done
```

Three different values alternate in the `machineName` field:

```
f653fbc25b3e
e1e8f67ddf49
e7fd09c1f735
f653fbc25b3e
...
```

Nginx spreads the requests across the replicas round-robin. The client has no idea -
it keeps calling the same address.

### 4. Each replica has its own memory

```bash
curl -X POST http://localhost:8080/api/todos \
     -H "Content-Type: application/json" \
     -d "{\"title\":\"Replication test\",\"priority\":\"High\"}"
```

Then several times:

```bash
for i in $(seq 1 8); do curl -s http://localhost:8080/api/info; echo; done
```

```
f653fbc25b3e -> 5 todos     <-- this is where the POST landed
e1e8f67ddf49 -> 4 todos
e7fd09c1f735 -> 4 todos
f653fbc25b3e -> 5 todos
```

**This is the point of the whole case study.** One replica handled the write and the other
two know nothing about it. For a user this means a newly created todo is sometimes
in the list and sometimes not, depending on where nginx sends them.

The fault is neither Docker's nor nginx's. It is in the application: it holds state
in its own memory (`InMemoryTodoRepository`), and such an application simply cannot
be run as multiple instances. Either the state moves out - into a database, a cache
or an object store - or there stays exactly one instance.

The rule: **only a service that remembers nothing itself can be replicated.**

### 5. Scaling while running

```bash
docker compose -f case-study-a-dockerfile/05-compose/compose.yaml up -d --scale api=5
```

Compose adds two containers and leaves the other three running. Check:

```bash
for i in $(seq 1 12); do curl -s http://localhost:8080/api/info; echo; done
```

Five distinct `machineName` values appear. Nginx found the new replicas by itself,
without a restart - thanks to this part of `nginx.conf`:

```nginx
resolver 127.0.0.11 valid=5s ipv6=off;
set $target "api:8080";
proxy_pass http://$target;
```

Had the target been written inline as `proxy_pass http://api:8080;`, nginx would
have resolved the name once at startup and never learned about the new replicas.
Going through a variable forces it to keep asking Docker's internal DNS (`127.0.0.11`).

Back to three: `docker compose ... up -d --scale api=3`.

### 6. Logs

```bash
cd case-study-a-dockerfile/05-compose

docker compose logs              # all services at once, colour-coded per service
docker compose logs -f api       # just the API, followed live
docker compose logs --tail 20 nginx
docker compose top               # processes in all containers
```

### 7. Cleaning up

```bash
docker compose -f case-study-a-dockerfile/05-compose/compose.yaml down
```

`down` stops and removes the containers **and the network** Compose created.
Plain `stop` would only stop the containers.

---

## Compose command summary

| Command | What it does |
|---|---|
| `docker compose up -d` | creates and starts everything in the background |
| `docker compose up -d --build` | the same, but rebuilds the images first |
| `docker compose ps` | service status |
| `docker compose logs -f <service>` | follow the logs |
| `docker compose exec <service> sh` | shell in the service's running container |
| `docker compose up -d --scale api=5` | change the replica count while running |
| `docker compose stop` / `start` | stop / start, keeping the containers |
| `docker compose down` | stop and remove containers and the network |
| `docker compose config` | print the resulting file with variables substituted |

---

## Takeaways

- `compose.yaml` describes the **whole environment**: services, networks, ports,
  dependencies. Instead of a series of long `docker run` commands you type
  `docker compose up`.
- Compose creates its own network, so services find each other by name.
- Only a stateless service can be replicated. Our API is not one, and it shows
  nicely what that costs in practice.
- `depends_on` waits only for the other service to **start**, not to be ready.
  Waiting for actual readiness via `condition: service_healthy` is covered in case study C.
