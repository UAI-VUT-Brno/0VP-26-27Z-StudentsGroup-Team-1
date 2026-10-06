# Persistent logging

The API uses Serilog for persistent application logging.

## Logging method and persistence target

Application logs are written both to the console and to log files.

The log files are stored in the `logs/` directory under the application content root.

When the API is run locally from this repository, the path is:

`src/UAI.ZeroVP.API/logs/`

The file name uses daily rolling, for example:

`uai-zerovp-20261006.log`

A new log file is therefore used for each day.

Generated `.log` files are ignored by Git and are not stored in the repository.

## Log contents

Persistent log entries contain:

- date and time
- log level
- event source
- event description
- event result where applicable
- exception information when an error occurs

HTTP API requests are logged with the HTTP method, request path, response status code and processing time.

The HTTP response status code represents the result of an API request.

Successful requests are logged as `Information`, client errors (`4xx`) as `Warning`, and server errors (`5xx`) or unhandled exceptions as `Error`.

Application start and stop are logged explicitly as lifecycle events with the result `Success`.

## Examples

Successful API request:

`2026-10-06 20:51:42.329 +02:00 [INF] [Serilog.AspNetCore.RequestLoggingMiddleware] HTTP GET /api/todos responded 200 in 220.3090 ms`

Failed API request:

`2026-10-06 20:34:54.749 +02:00 [WRN] [Serilog.AspNetCore.RequestLoggingMiddleware] HTTP GET /api/todos/00000000-0000-0000-0000-000000000000 responded 404 in 45.3989 ms`

Application start:

`2026-10-06 21:06:15.029 +02:00 [INF] [UAI.ZeroVP.API] Application lifecycle event Start completed with result Success.`

Application stop:

`2026-10-06 21:06:49.575 +02:00 [INF] [UAI.ZeroVP.API] Application lifecycle event Stop completed with result Success.`

For a server error, the request is logged at `Error` level with HTTP status `500`.

If an unhandled exception causes the error, the log also contains the exception type, message and stack trace.

During error-handling verification, an exception was recorded together with its request event, for example:

`System.InvalidOperationException: Logging test exception`
