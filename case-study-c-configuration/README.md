# Case study C - Configuring an application in a container

**Level: advanced.** Assumes you can write and read a Dockerfile and that `build`,
`run`, `logs` and `exec` give you no trouble. If you are unsure, work through
[case study A](../case-study-a-dockerfile/) and [case study B](../case-study-b-tooling/) first.

One image, many environments. That is precisely why containers exist: **the very
image you tested is the one that goes to production** - it differs only in what you
pour into it at start-up. This case study is entirely about that "what".

The `UAI.ZeroVP.API` application has two switches ready for it:

| Key | What it does |
|---|---|
| `Swagger:Enabled` | turns on the interactive documentation at `/swagger` |
| `Todo:SeedSampleData` | fills the store with sample todos at start-up |

| # | Example | What it covers |
|---|---|---|
| 1 | [Baseline](01-baseline/) | where an application in a container gets its settings from |
| 2 | [Environment variables](02-environment-variables/) | `-e`, the double underscore, `ENV` vs `-e` |
| 3 | [Settings files](03-settings-files/) | bind mounts, `--env-file`, **source precedence** |
| 4 | [Ports, networks, health](04-ports-networks-health/) | internal port, interface binding, read-only |
| 5 | [Docker Compose](05-compose/) | `environment`, `env_file`, profiles, `secrets` |

## Build context

```bash
docker build -f case-study-c-configuration/01-baseline/Dockerfile -t zerovp:c01 .
```

Examples 2 to 5 use the `zerovp:c01` image from example 1.

## A note for Git Bash on Windows

Git Bash rewrites absolute paths in arguments into Windows ones - `/app/file.json`
becomes `C:/Program Files/Git/app/file.json` and Docker then reports nonsense.
It shows up mainly with `-v` and with commands like `docker run ... cat /app/...`.

Either of these works around it:

```bash
MSYS_NO_PATHCONV=1 docker run -v "K:/path/file.json:/app/file.json:ro" ...
docker run -v "K:/path/file.json://app/file.json:ro" ...    # double slash
```

PowerShell and `cmd.exe` do not have this problem.
