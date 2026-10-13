# API response time

**Requirement:** every API endpoint must return a response to the client within **500 ms**,
also when several users use the API at the same time.

## Summary

- The API met the 500 ms limit with a large margin even before any change.
  The slowest request measured under load was 260 ms (a single logging stall); typical requests took 3–6 ms.
- Automated load tests enforce the limit for every endpoint with 50 concurrent users.
  They are opt-in, because their results depend on the machine and its current load.
- The running API logs a `Warning` for every request that exceeds the limit.
- Profiling showed that synchronous log writing was the main source of latency spikes.
  Writing logs asynchronously reduced the median response time by 14–29 % and the 95th percentile 3–5×.
  After the change the slowest measured request took 87 ms.

## How it is measured

### Automated load tests

The tests live in `tests/Performance/`:

| File | Purpose |
|---|---|
| `ApiResponseTimeTests.cs` | The load tests and the 500 ms assertion |
| `LoadRunner.cs` | Starts concurrent virtual users and times every HTTP request |
| `EndpointStatistics.cs` | Computes median (p50), 95th and 99th percentile (p95, p99) and maximum per endpoint |
| `PerformanceCollection.cs` | Runs the performance tests on their own, not in parallel with other tests |
| `PerformanceFactAttribute.cs` | Skips the tests unless `RUN_PERFORMANCE_TESTS=true` is set |
| `PerformanceApiFactory.cs` | Starts the API for the tests and redirects its log files to a temp directory |

The API is started in-process with `WebApplicationFactory` (package `Microsoft.AspNetCore.Mvc.Testing`).
**50 virtual users** run at the same time, and each of them repeats a scenario **20 times**:

- **read scenario:** `GET /api/todos`, `GET /api/todos?isCompleted=false`, `GET /api/todos/{id}`, `GET /api/info`, `GET /health`
- **write scenario:** `POST /api/todos` → `PUT /api/todos/{id}` → `POST /api/todos/{id}/complete` → `DELETE /api/todos/{id}`

That gives 1 000 measured requests per endpoint. Each request is timed from sending until the whole response
body has been read.

A test fails when:

- any request returns an error status code (4xx or 5xx), or
- the 99th percentile (p99) of any endpoint is above the limit.

The limit is read from `Performance:ResponseTimeBudgetMs` in `appsettings.json`, the same setting the API uses
for slow-request warnings. The tests assert on p99 rather than on the single slowest request: one request out of
1 000 can be delayed by the machine itself (OS scheduler, antivirus, file sync), which made the test fail
randomly on some computers. The maximum is still printed, with a note when it exceeds the limit, and slow
requests in the running API are reported by the log warning described below.

Before measuring, each scenario runs once with all 50 virtual users (warm-up). This compiles the code (JIT)
and lets the thread pool grow to the needed size before the measured run starts.
The cost of the first request after start is described separately below.

### Running the tests

The performance tests are **not part of the default `dotnet test` run**: their results depend on the machine
and its current load. Without the environment variable they are reported as skipped.

Run them on demand (PowerShell):

    $env:RUN_PERFORMANCE_TESTS = "true"; dotnet test -c Release --filter "Category=Performance" --logger "console;verbosity=detailed"

Bash:

    RUN_PERFORMANCE_TESTS=true dotnet test -c Release --filter "Category=Performance" --logger "console;verbosity=detailed"

Use the Release configuration and close other heavy applications for meaningful numbers.

During the tests the API writes about 8 400 log lines. They go to `%TEMP%/uai-zerovp-performance-tests/`
instead of `src/UAI.ZeroVP.API/logs/`.

### Monitoring the running API

Every HTTP request is logged together with its processing time (see [persistent-logging.md](persistent-logging.md)).
A request that succeeds but takes longer than the configured limit is logged as `Warning` instead of `Information`.
This also applies to `/health`, whose successful requests are otherwise logged only at `Verbose` level.

The limit is configured in `appsettings.json`:

```json
"Performance": {
  "ResponseTimeBudgetMs": 500
}
```

It can be overridden with the environment variable `Performance__ResponseTimeBudgetMs`.
For example, setting it to `1` makes every request appear as a warning, which is a quick way to check the feature:

    [WRN] HTTP GET /api/todos responded 200 in 83.0595 ms

## Results

### Method

The load tests were run three times before and three times after the optimization, back to back,
on the same machine and in the Release configuration. For the "before" runs the uncommitted change was
temporarily set aside with `git stash`. The tables show the **median of the three runs**,
because a single run can be affected by other activity on the machine.

### Response time measured by the client

50 concurrent users × 20 iterations, 1 000 requests per endpoint.
"Before" = synchronous log sinks, "after" = asynchronous log sinks.

| Endpoint | p50 before → after | p95 before → after | max before → after |
|---|---|---|---|
| `GET /api/info` | 4.15 → 2.94 ms | 46.2 → 8.6 ms | 52.6 → 31.1 ms |
| `GET /api/todos` | 3.12 → 2.64 ms | 31.5 → 10.4 ms | 53.1 → 49.4 ms |
| `GET /api/todos?isCompleted=false` | 3.94 → 2.97 ms | 50.8 → 15.5 ms | 53.1 → 41.2 ms |
| `GET /api/todos/{id}` | 3.99 → 2.95 ms | 37.1 → 10.9 ms | 53.2 → 35.3 ms |
| `GET /health` | 2.97 → 2.56 ms | 13.8 → 8.3 ms | 52.7 → 28.3 ms |
| `POST /api/todos` | 5.79 → 4.16 ms | 53.9 → 13.2 ms | 65.5 → 32.8 ms |
| `PUT /api/todos/{id}` | 5.47 → 4.22 ms | 53.4 → 14.2 ms | 65.4 → 31.8 ms |
| `POST /api/todos/{id}/complete` | 4.70 → 3.55 ms | 53.0 → 9.7 ms | 65.1 → 55.7 ms |
| `DELETE /api/todos/{id}` | 4.69 → 3.40 ms | 54.7 → 9.5 ms | 65.2 → 41.2 ms |

Slowest single request over all runs: **259.7 ms before**, **87.3 ms after**.

### Processing time measured by the server

The request log records how long the server spent on each request.
During the load tests the server needed about **0.2 ms** for a typical request (p50 0.15–0.5 ms,
p95 under 3 ms). The largest server-side times (about 100 ms) belong to the first request after start.

The difference between 0.2 ms on the server and 3–6 ms on the client is overhead outside the controller.
Before the optimization it included writing the log line, which the request had to wait for.

### First request after start

The first request after the application starts takes **80–110 ms** (Release build, real Kestrel server),
because the code is compiled by the JIT compiler on first use. With `dotnet run` in Debug it can take about
220 ms. Following requests take 3–5 ms. Even the first request is within the limit.

## Optimization: asynchronous log sinks

### Problem

Serilog wrote every log event to the console and to the log file synchronously, on the thread that handled
the request. The request could not finish until its log line had been written. Under concurrent load this
showed up as a p95 of 40–60 ms. In one run, all four write endpoints had the same maximum of about 259 ms:
a single stall while writing the log delayed every request that was running at that moment.
That one stall used more than half of the 500 ms limit.

### Change

The console and file sinks are wrapped in `WriteTo.Async` (package `Serilog.Sinks.Async`).
Log events are put into an in-memory queue and written by a background thread, so the request no longer waits
for the console or the disk. The log format, file location and rolling interval are unchanged.

### Effect

- p50 dropped by 14–29 %.
- p95 dropped 3–5×, from about 50 ms to about 10 ms.
- The slowest request dropped from 260 ms to 87 ms.

### Verification of persistent logging

Asynchronous writing could lose events that are still in the queue when the application stops.
This was checked explicitly:

- The application was started, sent a successful request and a `404` request, and stopped with Ctrl+C.
  The log file contained both requests (`INF` and `WRN`) and, as the last line,
  `Application lifecycle event Stop completed with result Success.`
- One run of the load tests (with the original single-user warm-up) produced 8 009 request log entries. All 8 009 were found in the log file,
  the same number as with synchronous sinks, so no events were lost under load.

### Trade-offs

- On a normal shutdown the queue is flushed. On a hard kill or crash, the last queued events may be lost.
- The queue holds 10 000 events by default. When it is full, new events are dropped instead of slowing requests down.
- Log lines appear in the file a few milliseconds later than with synchronous sinks.

## Considered and not changed

- **Caching data in `GET /api/info`.** The endpoint reads the assembly version and process start time
  on every request. Measurements showed it is not slower than the other endpoints, so it was left as it is.
- **More virtual users in the automated tests.** Higher numbers make the test suite slower for everyone,
  and because the test client and the API share one machine, they mostly measure the machine rather than the API.
  50 users without pauses is already well above the expected load.

## Limitations

- The automated tests run the API in-process. Network latency and the Docker network are not included.
- The test client and the API run on the same machine and compete for the CPU.
- Results depend on the hardware. The numbers above come from a single developer laptop.
- Container CPU limits (for example `docker run --cpus`) were not measured. With strong limits,
  especially the first request after start will be slower.

## Conclusion

The API meets the 500 ms requirement with a large margin, both before and after the optimization.
The requirement is now enforced by automated load tests and monitored at runtime through the request log.
Asynchronous log writing removed the main source of latency spikes without changing what is logged.