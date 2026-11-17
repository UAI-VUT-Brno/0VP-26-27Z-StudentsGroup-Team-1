# `/issue-to-pr`: agentic pack for Claude Code

An autonomous workflow that turns a GitHub issue of this repository into a verified pull request:

1. fetch the issue;
2. plan the change;
3. create a feature branch;
4. implement the C# code and tests across all layers;
5. run a self-healing build-and-test loop;
6. sync the docs;
7. open the PR with `gh`.

## Layout

```text
.claude/
├─ settings.json                  permission allowlist for unattended runs
└─ skills/issue-to-pr/            the pack (a Claude Code skills-directory plugin)
   ├─ .claude-plugin/plugin.json  plugin manifest, loads agents/ as subagents
   ├─ SKILL.md                    orchestrator, invoked as /issue-to-pr
   ├─ agents/                     subagents: planner, implementer, qa, release
   ├─ prompts/                    modular prompt library
   └─ rules/                      architecture and repository rules
```

The folder is a [skills-directory plugin](https://code.claude.com/docs/en/plugins/create#share-the-plugin-through-a-repository):
- its root `SKILL.md` is the `/issue-to-pr` command;
- the files in `agents/` load as the subagents `issue-to-pr:planner`, `issue-to-pr:implementer`, `issue-to-pr:qa` and `issue-to-pr:release`, each with its own tool list.

## Requirements

- [Claude Code](https://code.claude.com/docs/en/quickstart), started from the **repository root**, and the workspace trust dialog accepted (a repository plugin loads only in a trusted folder).
- [GitHub CLI](https://cli.github.com/) signed in with push rights (`gh auth status`).
- .NET 10 SDK.
- A clean working tree.

The plugin is part of the repository, so there is nothing to install. To check that it loaded, run `claude plugin list` in the shell or `/plugin` in a session. It is listed as `issue-to-pr@skills-dir`.

Plugins load when a session starts. If `/issue-to-pr` works but the `issue-to-pr:*` subagents are missing, the session was started before the plugin was available or before the folder was trusted. Run `/reload-plugins` or restart Claude Code. `/issue-to-pr` alone is not proof that the plugin loaded, because the root `SKILL.md` also loads as a plain skill.

## Usage

```text
/issue-to-pr #42
/issue-to-pr 42
/issue-to-pr https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/42
/issue-to-pr #42 --base benchmark/base-t1-10
```

`--base` selects the branch the work starts from and the PR targets. The default is `main`.

Once started, the run needs no input. Every command it uses is pre-approved in [`.claude/settings.json`](../../settings.json), and dangerous ones (`git push --force`, `git reset --hard`, `gh pr merge`, `gh issue close`, …) are denied. A command outside the allowlist would prompt, so a prompt during a run means the allowlist or an agent needs fixing.

## Stages

| # | Stage | Done by | Output |
|---|---|---|---|
| 0 | Parse input | orchestrator | issue number, base branch |
| 1 | Preflight | orchestrator | clean tree, `gh` signed in, base up to date |
| 2 | Ingest | orchestrator | `.issue-to-pr/<n>/issue.json` |
| 3 | Plan | `planner` (read-only) | `.issue-to-pr/<n>/plan.md` |
| 4 | Branch | orchestrator | `feat/t1-<n>-<short-slug>` |
| 5 | Implement | `implementer` | commits per layer |
| 6 | Verify & repair | `qa` | `dotnet test --configuration Release`, at most 4 attempts |
| 7–8 | Docs sync & release | `release` | push, PR with `Closes #<n>` (draft if tests still fail) |
| 9 | Summary | orchestrator | final report, `.issue-to-pr/<n>/run.md` |

The run folder `.issue-to-pr/<n>/` is git-ignored. Its `run.md` keeps a timestamped log of the stages, the first-attempt result and the number of repair cycles, which the benchmark uses.

Stop conditions and the exact behaviour of each stage are in [SKILL.md](SKILL.md).

## Mapping to the assignment deliverables

| Brief §5 | Here |
|---|---|
| `skills/issue-to-pr/SKILL.md` | [SKILL.md](SKILL.md) |
| `skills/issue-to-pr/agents/` | [agents/](agents/) + tool scopes in their frontmatter |
| `skills/issue-to-pr/prompts/` | [prompts/](prompts/) |
| `skills/issue-to-pr/rules/` | [rules/](rules/) |
| Tool permissions | [`.claude/settings.json`](../../settings.json) |
