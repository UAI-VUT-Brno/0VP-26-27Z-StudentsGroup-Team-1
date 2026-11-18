---
name: issue-to-pr
description: Implement a GitHub issue of this repository end-to-end and open a verified pull request. Runs only when the user types /issue-to-pr.
argument-hint: "#<issue> | <issue> | <issue URL> [--base <branch>]"
disable-model-invocation: true
---

# /issue-to-pr

You are the **orchestrator** of an autonomous workflow. It takes one GitHub issue of this repository and turns it into a verified pull request. You run the stages below in order and delegate the specialised work to the subagents of this plugin.

Raw arguments: `$ARGUMENTS`

## Operating rules

- **No human in the loop.** Do not ask the user anything, and do not wait for confirmation between stages. If a stage cannot continue, stop as described in [Stop conditions](#stop-conditions).
- **Shell:** run every shell command with the **Bash** tool, one command per call. Do not chain commands with `&&`, `;` or `|` unless a stage says so. The permission allowlist in `.claude/settings.json` matches single commands. Run only the commands named in the stages, and use Read, Grep and Glob instead of shell commands for files. Any other command waits for approval and stalls the run.
- **Working directory:** always the repository root. Never use `cd`.
- **Run folder:** keep all run data in `.issue-to-pr/<n>/`, where `<n>` is the issue number. The folder is git-ignored. Create it with the Write tool, not with `mkdir`.
- **Run log:** append one line per stage to `.issue-to-pr/<n>/run.md` in the form `- <UTC time> | <stage> | <result>`. Get the time with `date -u +%H:%M:%S`.
- **Pack folder:** the agents read their prompt from [prompts/](prompts/) and their rules from [rules/](rules/). Pass the pack folder `.claude/skills/issue-to-pr/` to every subagent.
- **Never** force-push, merge a PR, close or edit an issue, rewrite history, delete branches, or change files outside the plan.

## Stage 0 – Parse the input

Parse `$ARGUMENTS`:

| Form | Example | Issue number |
|---|---|---|
| `#<n>` | `#42` | `42` |
| `<n>` | `42` | `42` |
| issue URL of this repository | `https://github.com/<owner>/<repo>/issues/42` | `42` |

- Optional `--base <branch>` anywhere after the issue sets the base branch. The default is `main`.
- For a URL, `<owner>/<repo>` must equal the output of `gh repo view --json nameWithOwner --jq .nameWithOwner`.
- Anything else (empty input, several issues, a PR URL, a non-numeric value, a URL of another repository) is **invalid**. Stop with the message `Invalid input. Usage: /issue-to-pr #<issue> | <issue> | <issue URL> [--base <branch>]`, before any git command runs.

## Stage 1 – Preflight

Run these checks. Any failure stops the run before a branch is created.

1. `git status --porcelain`: the output must be empty. Otherwise stop with `Working tree is not clean`.
2. `gh auth status`: it must succeed.
3. `git fetch origin <base>`: the base branch must exist on `origin`. This also makes a base that exists only on `origin` (for example `benchmark/base-t1-10`) available locally.
4. `git switch <base>`. If the branch exists only on `origin`, `git switch` creates a local tracking branch.
5. `git pull --ff-only origin <base>`. The base branch must be up to date with `origin`.

Create `.issue-to-pr/<n>/run.md` with the heading `# Run of /issue-to-pr #<n>` and log `preflight | ok`.

## Stage 2 – Ingest the issue

1. `gh issue view <n> --json number,title,body,labels,state`
2. If the issue does not exist, stop. If `state` is not `OPEN`, stop with `Issue #<n> is closed`.
3. Save the JSON to `.issue-to-pr/<n>/issue.json` with the Write tool.
4. Log `ingest | <title>`.

## Stage 3 – Plan

Delegate to the subagent **`issue-to-pr:planner`**. Give it:
- the issue number and the path `.issue-to-pr/<n>/issue.json`,
- the pack folder `.claude/skills/issue-to-pr/`.

The planner is read-only and returns the plan as its final message. Save that message to `.issue-to-pr/<n>/plan.md`.

If the planner reports that the issue is not implementable (for example, no acceptance criteria can be derived, or it asks for something outside this repository), stop with its reason and do not create a branch.

Log `plan | <number of files to change>`.

## Stage 4 – Branch

1. Derive `<short-slug>` from the issue title:
   - drop a leading `0VP-T1-NN` prefix;
   - lowercase, ASCII only;
   - words joined with `-`;
   - at most 5 words and 40 characters.
2. The branch name is `feat/t1-<n>-<short-slug>`.
3. If `git rev-parse --verify --quiet feat/t1-<n>-<short-slug>` or `git ls-remote --heads origin feat/t1-<n>-<short-slug>` finds it, stop with `Branch already exists`.
4. Run `git switch -c feat/t1-<n>-<short-slug>`.

Log `branch | <branch name>`.

## Stage 5 – Implement

Delegate to the subagent **`issue-to-pr:implementer`**. Give it:
- the issue number,
- the paths `.issue-to-pr/<n>/issue.json` and `.issue-to-pr/<n>/plan.md`,
- the pack folder.

It implements the plan layer by layer (DTO → Data → API), writes the tests, and commits with Conventional Commits. It returns the list of commits and any deviations from the plan.

Log `implement | <number of commits> commits`. If it reported deviations, copy them to `run.md` under a `## Deviations from the plan` heading, so the release agent can put them in the PR.

## Stage 6 – Verify and repair

Delegate to the subagent **`issue-to-pr:qa`**. Give it:
- the issue number,
- the path `.issue-to-pr/<n>/plan.md`,
- the pack folder,
- the attempt ceiling **4**.

It runs `dotnet test --configuration Release`, repairs failures and reruns, up to 4 attempts in total. It returns:
- `status`: `passed` or `failed`
- `first_attempt_passed`: `yes` or `no`
- `repair_cycles`: a number from 0 to 3
- `tests`: total, passed, failed and skipped from the last run
- `remaining_failures`: only when `status` is `failed`

Copy these values to `run.md` under a `## Verification` heading. Log `verify | <status>, <repair_cycles> repair cycles`.

## Stage 7 and 8 – Docs sync and release

Delegate to the subagent **`issue-to-pr:release`**. Give it:
- the issue number, branch, base branch and issue title,
- the paths `.issue-to-pr/<n>/issue.json`, `.issue-to-pr/<n>/plan.md` and `.issue-to-pr/<n>/run.md`,
- the pack folder.

It:
1. updates `docs/` and `README.md` when a public contract or configuration changed;
2. runs `git diff --check <base>...HEAD`;
3. pushes the branch;
4. opens the PR against `<base>`.

If verification **failed**, it opens the PR as a **draft** with the failure report, so the result stays visible. It returns the PR URL.

Log `release | <PR URL>`.

## Stage 9 – Final summary

End the run with this message, and nothing else:

```text
/issue-to-pr #<n> finished: <passed | failed>
Issue:     #<n> <title>
Branch:    <branch>  →  <base>
Commits:   <count>
Tests:     <passed>/<total> passed (first attempt: <yes|no>, repair cycles: <k>)
PR:        <URL> (<ready | draft>)
Run log:   .issue-to-pr/<n>/run.md
```

## Stop conditions

| Condition | Stage | Effect |
|---|---|---|
| Invalid input | 0 | stop, nothing changed |
| Dirty working tree, `gh` not authenticated, base missing on `origin` or not fast-forwardable | 1 | stop, nothing changed |
| Issue missing or closed | 2 | stop, nothing changed |
| Issue not implementable | 3 | stop, no branch |
| Branch already exists | 4 | stop, no branch |
| Tests still failing after 4 attempts | 6 | continue to release as a **draft** PR |
| Push or PR creation fails | 8 | stop, report the error, keep the local branch |

When you stop, print `/issue-to-pr #<n> stopped at stage <k>: <reason>` and log the same line to `run.md` if it exists.
