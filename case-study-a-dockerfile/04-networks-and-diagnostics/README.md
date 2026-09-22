# A4 - Networks and diagnostics

## Goal

Run two containers, let them talk to each other, and learn how to look inside a
running container. At the same time, confirm first-hand that **data in this
application is ephemeral** - by far the most important lesson of the whole case study.

This example needs no new Dockerfile. It uses the `zerovp:a03` image from example 3.

## Assignment

1. Create a network called `zerovp-net` and start two containers in it,
   `api-one` and `api-two`, reachable from outside on ports 8081 and 8082.
2. From `api-one`, call `api-two` **by name**, not by IP address.
3. Create a todo through port 8081, then check the todo count on both ports.
   Explain the difference.
4. Restart `api-one` and look at the count again. What happened and why?
5. Try the same call by name from a container that is **not** in your network.
6. Work through the diagnostic commands: `logs`, `top`, `stats`, `port`, `inspect`, `diff`.

Expected result: containers find each other by name, hold independent data, and
lose it on restart.

---

## Solution

### 1. A custom network and two containers

```bash
docker network create zerovp-net

docker run -d --name api-one --network zerovp-net -p 8081:8080 zerovp:a03
docker run -d --name api-two --network zerovp-net -p 8082:8080 zerovp:a03
```

Both containers listen on 8080 internally. They differ only in which host port we
map them to. Internally neither knows anything special about the other.

```bash
docker network inspect zerovp-net --format "{{range .Containers}}{{.Name}} {{.IPv4Address}}{{println}}{{end}}"
```

```
api-two   172.22.0.3/16
api-one   172.22.0.2/16
```

### 2. Calling by name

```bash
docker exec api-one curl -s http://api-two:8080/api/info
```

```json
{"machineName":"5869c228ceca","environment":"Production", ...,"todoCount":4}
```

This works because **a user-defined network has a built-in DNS server**. The
container name is also its network name. You never had to look up an IP address -
and you should not, because it changes on every start.

Note the port: from outside we use 8081, but from inside the network **8080**.
The `-p` mapping applies only to the host; between containers the real application
port is used.

### 3. Independent data

```bash
curl -X POST http://localhost:8081/api/todos \
     -H "Content-Type: application/json" \
     -d "{\"title\":\"Stored in api-one\",\"priority\":\"High\"}"

curl http://localhost:8081/api/info    # todoCount: 5
curl http://localhost:8082/api/info    # todoCount: 4
```

The todo appeared in only one of them. Both instances run from the **same image**,
but each has its own memory. The image is a shared template; state is private to
each container.

### 4. Ephemeral data

```bash
docker restart api-one
curl http://localhost:8081/api/info    # todoCount: 4
```

The todo is gone for good. The application keeps data in a `ConcurrentDictionary`
inside the process (see `src/UAI.ZeroVP.Data/InMemory/InMemoryTodoRepository.cs`) -
restarting the process destroys that memory.

**This is the core of the example.** A container is deliberately disposable: it gets
removed, recreated, moved to another machine. Anything that must survive has to live
outside it - in a mounted volume, a database, an object store. Our application does
none of that, which is exactly why it behaves this way.

Look at what the container's write layer actually holds:

```bash
docker diff api-one   # what changed relative to the image
```

```
C /tmp
A /tmp/clr-debug-pipe-1-72466-in
A /tmp/dotnet-diagnostic-1-72466-socket
```

Just a few temporary .NET files. No data.

### 5. A container outside the network

```bash
# inside our network - works
docker run --rm --network zerovp-net curlimages/curl -s -o /dev/null \
       -w "%{http_code}\n" http://api-one:8080/health
# 200

# on the default bridge network - the name does not resolve
docker run --rm curlimages/curl -s --max-time 5 -o /dev/null \
       -w "%{http_code}\n" http://api-one:8080/health
# 000  (connection never established)
```

The default `bridge` network cannot resolve names - a leftover from the days before
user-defined networks. **Always create your own network.** You get DNS and isolation
with it: anything not on the same network cannot see you.

### 6. Diagnostic commands

| Command | What it is for |
|---|---|
| `docker logs -f --tail 20 api-one` | application output; `-f` follows it live |
| `docker exec -it api-one sh` | interactive shell inside the running container |
| `docker top api-one` | processes inside the container |
| `docker stats api-one api-two` | live CPU and memory usage |
| `docker port api-one` | how the ports are mapped |
| `docker inspect api-one` | complete configuration as JSON |
| `docker diff api-one` | what changed in the filesystem relative to the image |

Useful variants:

```bash
# just the essentials from inspect
docker inspect --format "{{.State.Status}} {{.State.Health.Status}}" api-one

# processes: note UID 1654 - that is the app user from example 3
docker top api-one

# usage without live refresh
docker stats --no-stream --format "{{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}" api-one api-two
```

```
api-one   0.04%   30.14MiB / 31.04GiB
api-two   0.02%   30.41MiB / 31.04GiB
```

Thirty megabytes of memory per instance. For comparison, a separate virtual machine
running Windows or Linux would ask for hundreds of megabytes to several gigabytes.

A shell inside the container:

```bash
docker exec -it api-one sh
# inside:
ls -la /app
id
env | sort
exit
```

> Careful: anything you change this way disappears when the container is removed.
> A shell is for diagnosis, not for repairs. Repairs belong in the Dockerfile.

### Cleaning up

```bash
docker rm -f api-one api-two
docker network rm zerovp-net
```

---

## Takeaways

- A user-defined network gives containers **name resolution**. Never work with their
  IP addresses.
- `-p` mapping applies between host and container. Inside the network the real
  application port is used.
- One image, many containers, **and each has its own state**.
- A container is disposable. Anything that must survive a restart has to be stored
  outside it.
- `logs`, `exec`, `inspect` and `stats` are your first four moves when something breaks.
