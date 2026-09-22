# A2 - Multi-stage build and the ignore file

## Goal

Shrink the image from example 1 and speed up repeated builds. Understand what a
**layer** is and how the **cache** works.

Every `COPY` and `RUN` line creates a new layer. Docker stores layers and reuses
them on the next build - but only up to the first change it finds.
**From the first difference downward, everything is recomputed.** That is why the
order of the lines matters.

## Assignment

1. Compare the `Dockerfile` here with the one from example 1. Find three differences.
2. Build `zerovp:a02` and compare its size with `zerovp:a01`.
3. Verify the application still works.
4. Run the build a **second** time with no changes. How many steps came from the cache?
5. Change anything in `src/UAI.ZeroVP.API/Controllers/InfoController.cs` (a comment is
   enough) and rebuild. Which steps were recomputed and which were not?
6. Read `Dockerfile.dockerignore` and explain why `bin/` and `obj/` must not reach
   the image.

Expected result: an image around 340 MB; on a code change `dotnet restore` is skipped.

---

## Solution

### 1. Three differences

| | Example 1 | Example 2 |
|---|---|---|
| Stages | one | two - `build` and `runtime` |
| Runtime base image | `sdk:10.0` (1.3 GB) | `aspnet:10.0` (340 MB) |
| Source copying | `COPY . .` all at once | `.csproj` first, `restore`, then the rest |

### 2. Size

```bash
docker build -f case-study-a-dockerfile/02-multi-stage-build/Dockerfile -t zerovp:a02 .
docker images "zerovp"
```

```
REPOSITORY   TAG   SIZE
zerovp       a01   1.34GB
zerovp       a02   343MB
```

**Under a quarter.** Only the second stage made it into the final image - the
runtime plus the compiled application. The first stage, with the whole SDK and the
source code, did the compiling and was then discarded.

A secondary but important benefit: the final image **contains none of your source
code**. Whoever obtains the image does not get your `.cs` files with it.

### 3. Verifying the runtime

```bash
docker run -d --name zerovp-a02 -p 8080:8080 zerovp:a02
curl http://localhost:8080/health
docker rm -f zerovp-a02
```

### 4. Second build, nothing changed

```bash
docker build -f case-study-a-dockerfile/02-multi-stage-build/Dockerfile -t zerovp:a02 .
```

Every step prints `CACHED` and the build finishes in about a second. Docker did
nothing - it only confirmed the inputs had not changed.

### 5. Build after a code change

Add a comment to `InfoController.cs` and rebuild. The output shows:

```
[build 3/9] COPY UAI.ZeroVP.sln .                       CACHED
[build 4/9] COPY src/UAI.ZeroVP.DTO/...csproj           CACHED
[build 5/9] COPY src/UAI.ZeroVP.Data/...csproj          CACHED
[build 6/9] COPY src/UAI.ZeroVP.API/...csproj           CACHED
[build 7/9] RUN dotnet restore UAI.ZeroVP.sln           CACHED     <-- the point
[build 8/9] COPY . .                                               <-- the change is here
[build 9/9] RUN dotnet publish ...                                 <-- and therefore here too
```

`dotnet restore` downloads packages over the network and is the slowest step of the
whole build. Because it sits **above** the `COPY . .` line, a source change cannot
reach it and it comes from the cache.

Had we copied everything at once as in example 1, editing a single character
would mean downloading every package again.

Try the opposite too: change something in `UAI.ZeroVP.API.csproj`. Now `restore`
has to run - and rightly so, because the dependencies may have changed.

### 6. The ignore file

Measured on this project:

| | Without ignoring | With ignoring |
|---|---|---|
| Context size | 3.9 MB | 144 kB |
| File count | 145 | 23 |

Why `bin/` and `obj/` do not belong in the image:

- **They are compiled for Windows.** The container runs on Linux; they are useless.
- **They slow the build down.** The whole context is shipped to Docker before building.
- **They break the cache.** Build the project in Visual Studio, `obj/` changes, and
  Docker considers the context modified even though you touched no code.
- **They can overwrite the result.** Foreign binaries would end up in `/src`.

#### Where the ignore file belongs

Docker looks in two places, in this order:

1. next to the Dockerfile as `<dockerfile name>.dockerignore` - here `Dockerfile.dockerignore`
2. in the context root as `.dockerignore`

This course uses the first form, because all examples share one context and each
needs its own rules. **In a normal project put `.dockerignore` in the repository
root** - that is where everyone expects it.

---

## Takeaways

- A multi-stage build separates *what you compile with* from *what you run in*.
  Only the second reaches the result.
- Order layers from least to most volatile: `.csproj` and `restore` first, source
  code afterwards.
- Changing one layer invalidates every layer below it.
- `.dockerignore` belongs in every project. At minimum `bin/`, `obj/`, `.git/`.
