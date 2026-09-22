# B4 - Tags and registries

## Goal

Stop working with images purely locally. Learn to tag them sensibly and ship them
somewhere a colleague, a build server or a target machine can fetch them from.

A **registry** is an image store. Docker Hub is only one of many - you can start
your own in thirty seconds, which is what we do now.

## Assignment

1. Start a local registry on port 5555.
2. Tag the image `zerovp:b01` three times: `1.4.2`, `1.4` and `latest`,
   in the repository `uai/zerovp`.
3. Push all the tags to the registry.
4. Use the HTTP API to find out what the registry holds.
5. Delete the image locally and pull it back. Verify it works.
6. Try moving an image without a registry - as a file.
7. Work out why the `latest` tag is dangerous in production.

---

## Solution

### 1. Your own registry

```bash
docker run -d --name registry-lab -p 5555:5000 registry:3
curl http://localhost:5555/v2/
# {}          an empty answer with status 200 means the registry is up
```

Host port 5555 maps to 5000 inside - 5000 is the registry default.

> A registry started this way has **no encryption and no authentication**, and it
> keeps its data inside the container. Fine for a lab, unusable in production.

### 2. Tagging

```bash
docker tag zerovp:b01 localhost:5555/uai/zerovp:1.4.2
docker tag zerovp:b01 localhost:5555/uai/zerovp:1.4
docker tag zerovp:b01 localhost:5555/uai/zerovp:latest

docker images "localhost:5555/uai/zerovp"
```

```
REPOSITORY                       TAG     IMAGE ID       SIZE
localhost:5555/uai/zerovp        1.4     2e169b42e06a   352MB
localhost:5555/uai/zerovp        1.4.2   2e169b42e06a   352MB
localhost:5555/uai/zerovp        latest  2e169b42e06a   352MB
```

All three share the **same `IMAGE ID`**. `docker tag` copies nothing - it only adds
another name to the same image. The apparent `352 MB x 3` is an illusion; on disk
it exists once.

#### How the name is put together

```
localhost:5555 / uai       / zerovp       : 1.4.2
|_ registry _|   |_ name _|   |_ repo _|    |_ tag _|
                  space
```

Leave the registry out and Docker fills in Docker Hub. That is why `docker pull nginx`
pulls from Docker Hub - and why a name for your own registry must always carry the
address.

### 3. Pushing

```bash
docker push localhost:5555/uai/zerovp:1.4.2
docker push --all-tags localhost:5555/uai/zerovp
```

```
11d10a5080bc: Pushed
e538bd552492: Layer already exists         <-- already there, not sent again
1.4.2: digest: sha256:9f3c41d7b8e5... size: 856
```

This digest is **not** the `IMAGE ID` from the table above. The image ID names the local
image configuration; this one names the manifest stored in the registry. Two digests
over two different things - using one where the other is wanted fetches nothing.

A registry stores **layers**, not whole images. When you push the second version of
your application, only the single changed layer holding it travels (1.81 MB), not
the full 352 MB. That is another reason to keep the volatile parts of a Dockerfile
as low as possible.

### 4. What the registry holds

The registry has a simple HTTP API:

```bash
curl http://localhost:5555/v2/_catalog
# {"repositories":["uai/zerovp"]}

curl http://localhost:5555/v2/uai/zerovp/tags/list
# {"name":"uai/zerovp","tags":["1.4","1.4.2","latest"]}
```

Handy when a script needs to find out which versions are available.

### 5. Simulating another machine

```bash
# remove the local tags and fetch the image back from the registry
docker rmi localhost:5555/uai/zerovp:1.4.2 \
           localhost:5555/uai/zerovp:1.4 \
           localhost:5555/uai/zerovp:latest

docker pull localhost:5555/uai/zerovp:1.4.2

docker run -d --name b04 -p 8080:8080 localhost:5555/uai/zerovp:1.4.2
curl http://localhost:8080/api/info
# {"version":"1.4.2", ...}
docker rm -f b04
```

The pull finishes instantly and reports `Already exists` for every layer - `zerovp:b01`
still holds them locally, so only the tag came back. On a genuinely clean machine the
layers themselves would travel over the network.

The application runs without you having the source code or the .NET SDK available.
**That is the whole point of containers:** compile once, run anywhere.

### 6. Moving an image without a registry

When there is no registry (an air-gapped network, a customer machine with no
outbound access):

```bash
docker save -o zerovp-1.4.2.tar zerovp:b01
# the file comes out around 352 MB - the size the image has locally

# on the other machine
docker load -i zerovp-1.4.2.tar
# Loaded image: zerovp:b01
```

**`docker save` does not compress.** It writes the layers out as they are, so the tar is
about as large as `docker images` reports. That is a different number from the one a
`push` or `pull` moves: a registry keeps layers gzipped, so the same image crosses the
network considerably smaller. If the file has to travel, compress it yourself:

```bash
docker save zerovp:b01 | gzip > zerovp-1.4.2.tar.gz
docker load -i zerovp-1.4.2.tar.gz          # load unpacks gzip on its own
```

> Do not confuse this with `docker export` / `docker import`. Those work on a
> **container**, not an image - they produce a flat filesystem snapshot with no
> layers and no metadata, so even `ENTRYPOINT` is lost. For moving images you want
> `save` and `load`.

### 7. Why `latest` is dangerous

`latest` is **not** "the newest". It is an ordinary tag that merely gets filled in
when you name none. Nothing guarantees it points at the most recent version - it
points wherever someone last moved it.

The problems that follow:

- **You do not know what is running.** `docker ps` shows `latest` and that is all.
- **You cannot roll back.** When the new version misbehaves, there is nothing to
  return to.
- **Two machines, two versions.** Each pulled `latest` at a different moment.

A sane approach:

```bash
# tag with a concrete version and the moving tags at build time
docker build -f case-study-b-tooling/01-lifecycle/Dockerfile \
             --build-arg APP_VERSION=1.4.2 \
             -t localhost:5555/uai/zerovp:1.4.2 \
             -t localhost:5555/uai/zerovp:1.4 \
             -t localhost:5555/uai/zerovp:latest .
```

- `1.4.2` - **immutable**, never overwritten, this is what you deploy
- `1.4` - moves with patch releases
- `latest` - convenience while experimenting, never in production

Full certainty comes from the digest, which is derived from the content:

```bash
# the digest from the push output - not the IMAGE ID
docker pull localhost:5555/uai/zerovp@sha256:9f3c41d7b8e5...
```

You never have to copy it by hand: `docker inspect <image> --format "{{index .RepoDigests 0}}"`
prints the digest of an image you already hold.

### Registries with authentication

Real registries require you to log in. The procedure is always the same:

```bash
docker login <registry address>
docker tag zerovp:b01 <address>/<namespace>/zerovp:1.4.2
docker push <address>/<namespace>/zerovp:1.4.2
```

Docker stores the credentials in `~/.docker/config.json`. **Never use your personal
password on a build server** - create an access token scoped to that repository only.

### Cleaning up

```bash
docker rm -f registry-lab
docker rmi $(docker images -q "localhost:5555/uai/zerovp") 2>/dev/null
```

Removing the registry container also removes every image pushed into it - it runs
without a volume.

---

## Takeaways

| Command | What for |
|---|---|
| `docker tag source target` | gives an image another name, copies nothing |
| `docker push <name>` | sends it to a registry |
| `docker pull <name>` | fetches it from a registry |
| `docker login <registry>` | authentication |
| `docker save -o file.tar img` | writes an image to a file |
| `docker load -i file.tar` | reads an image from a file |

- A tag is not a version. Tag with a concrete number and treat `latest` as
  convenience, not a guarantee.
- A registry transfers only the missing layers. The instruction order in your
  Dockerfile decides how much data crosses the network.
- `save`/`load` works on images, `export`/`import` on containers. Mixing them up
  does not end well.
