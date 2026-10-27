# Persistent logging

The API uses Serilog for persistent application logging.

## Logging method and persistence target

Application logs are written both to the console and to log files.

The log file path is configurable using the `PersistentLogging:FilePath` setting.

The default value in `appsettings.json` is:

`logs/uai-zerovp-.log`

Relative paths are resolved from the application content root.

When the API is run locally from this repository with the default configuration, the logs are therefore stored in:

`src/UAI.ZeroVP.API/logs/`

The file sink uses daily rolling. For example:

`uai-zerovp-20261008.log`

A new log file is used for each day.

Generated `.log` files are ignored by Git and are not stored in the repository.

### Configuring the log path

The path can be changed in configuration using:

`PersistentLogging:FilePath`

It can also be overridden using an environment variable:

`PersistentLogging__FilePath`

For example:

`PersistentLogging__FilePath=/logs/uai-zerovp-.log`

This is useful when running the application in Docker.

### Docker persistence

Files written only inside a container are removed when the container is removed.

To keep log files outside the lifetime of the container, configure the log path to point to a mounted volume.

For example, the following options can be added to an existing `docker run` command:

    -e PersistentLogging__FilePath=/logs/uai-zerovp-.log
    -v uai-zerovp-logs:/logs

This stores the log files in the `uai-zerovp-logs` Docker volume instead of only in the container filesystem.

## Log contents

Persistent log entries contain:

- date and time
- log level
- instance name (`Instance:Name`, see [multiple-instances.md](multiple-instances.md))
- event source
- event description
- event result where applicable
- exception information when an error occurs

HTTP API requests are logged with the HTTP method, request path, response status code and processing time.

The HTTP response status code represents the result of an API request.

Successful requests are logged as `Information`, client errors (`4xx`) as `Warning`, and server errors (`5xx`) or unhandled exceptions as `Error`.

Successful `/health` requests are logged at `Verbose` level so that frequent Docker health checks do not fill the persistent log. Failed health requests are still logged according to their HTTP status.

Application start and stop are logged explicitly as lifecycle events with the result `Success`.

## Several instances

The log file is opened in shared mode (`shared: true`). Several instances – processes on one machine, or container replicas with a shared log volume – can therefore write into the same file at once without locking each other out. The instance name in every line tells them apart. The console output contains the instance name too.

## Examples

Successful API request:

`2026-10-06 20:51:42.329 +02:00 [INF] [api-5001] [Serilog.AspNetCore.RequestLoggingMiddleware] HTTP GET /api/todos responded 200 in 220.3090 ms`

Failed API request:

`2026-10-06 20:34:54.749 +02:00 [WRN] [api-5001] [Serilog.AspNetCore.RequestLoggingMiddleware] HTTP GET /api/todos/00000000-0000-0000-0000-000000000000 responded 404 in 45.3989 ms`

Application start:

`2026-10-06 21:06:15.029 +02:00 [INF] [api-5001] [UAI.ZeroVP.API] Application lifecycle event Start completed with result Success.`

Application stop:

`2026-10-06 21:06:49.575 +02:00 [INF] [api-5001] [UAI.ZeroVP.API] Application lifecycle event Stop completed with result Success.`

For a server error, the request is logged at `Error` level with HTTP status `500`.

If an unhandled exception causes the error, the log also contains the exception type, message and stack trace.

During error-handling verification, an exception was recorded together with its request event, for example:

`System.InvalidOperationException: Logging test exception`
