# Rules

These rules ground the `/issue-to-pr` agents in how this repository actually works. They describe the code on `main`. If a rule and the code disagree, the code wins: report the mismatch in the PR and do not "fix" unrelated code.

| File | Covers |
|---|---|
| [architecture.md](architecture.md) | DTO → Data → API layering, where code belongs, repository changes, API conventions, configuration, MCP |
| [concurrency.md](concurrency.md) | optimistic concurrency with `version` and `409 Conflict` |
| [csharp-style.md](csharp-style.md) | encoding (UTF-8 BOM, CRLF), language of comments and messages, C# conventions, build warnings |
| [testing.md](testing.md) | test layout, naming, what every change must test, what a test fix must never do |
| [repository.md](repository.md) | branches, commit types and scopes, every commit builds, docs to update, PR format |

## Which agent reads what

| Agent | Rules |
|---|---|
| `planner` | all |
| `implementer` | all |
| `qa` | `testing.md`, `csharp-style.md`, `architecture.md`, `repository.md` (commits) |
| `release` | `repository.md` |
