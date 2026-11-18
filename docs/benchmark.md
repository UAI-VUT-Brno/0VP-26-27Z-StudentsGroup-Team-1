# Copilot and /issue-to-pr benchmark

## Cases and fixed baselines

| Case | Issue | Pull request base | Pinned commit |
| --- | --- | --- | --- |
| Historical — Add user identification to tasks | [#53](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/53), copied from [#19](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/19) | `benchmark/base-t1-10` | `d562bdae94293fc044bc0353c2651f56d498b443` |
| Synthetic — Optimistic concurrency for tasks | [#54](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/54) | `benchmark/base-synthetic` | `d7466ebe8ff58b26bdf6f0a808d3172d467f0320` |

The historical commit is the tip of `main` before PR #29. The synthetic commit is the tip of `main` when these fixtures were prepared. Keep both refs pinned and target the corresponding base in all benchmark PRs. Keep the fixture issues open through the four runs.

### Baseline verification

Both commits built in Release with .NET SDK 10.0.403, with zero warnings and errors. These are setup checks, separate from the four measured runs.

| Base | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `benchmark/base-t1-10` | 287 | 0 | 2 |
| `benchmark/base-synthetic` | 325 | 0 | 2 |

```sh
dotnet build UAI.ZeroVP.sln --configuration Release
dotnet test UAI.ZeroVP.sln --configuration Release --no-build
```

Both skips are existing opt-in load tests.

## Run procedure

Start after the benchmark protocol is merged. Approach B also requires the merged orchestrator, rules, subagents and prompts. For each case, complete A before opening B's solution. Use an independent fresh clone per run, start on the base branch shown above, and confirm that `git rev-parse HEAD` matches the pinned commit and `git status --porcelain` is empty. Use the same issue text, machine, .NET SDK and environment for both approaches. Record any deviations with the result. Capture tool versions and the selected model in each run. Use the same package-cache policy across all four runs.

- **A — Interactive GitHub Copilot in VS Code.** Use inline completions, Copilot Chat with workspace context (`@workspace`), and manual debugging. Use a distinct working branch such as `benchmark/copilot-53` or `benchmark/copilot-54` and open the PR against the case's base. Capture the screen or keep timestamped notes of prompts, edits, commands and test outcomes. Record VS Code, Copilot and model versions or identifiers.
- **B — Claude Code `/issue-to-pr`.** Take the complete `.claude/skills/issue-to-pr/` pack and `.claude/settings.json` from one pinned commit containing the merged workflow. Neither benchmark base contains the pack. Copy those files into each B clone at their expected paths before timing, and locally exclude `/.claude/` and `/.issue-to-pr/` in that clone's `.git/info/exclude`. Record the workflow commit. Verify the working tree is clean, `gh auth status` succeeds, the plugin and its four agents are available, and workspace trust is already set. Start the screen recording and enter the single command below. Leave the session untouched until it finishes. Export the transcript and preserve `.issue-to-pr/<issue>/run.md` along with the PR link. Record Claude Code, GitHub CLI and model versions or identifiers.

```text
/issue-to-pr #53 --base benchmark/base-t1-10
/issue-to-pr #54 --base benchmark/base-synthetic
```

## Metrics

| Metric | How to measure it |
| --- | --- |
| Total time to a green PR | Record UTC start when the issue is first opened for the run and UTC end once both the PR exists and the Release suite has passed for its submitted code. For B, record when the runner opens the issue immediately before entering the command. Use the same local Release check for both tools and record CI separately if available. If the run ends without a green PR, record the elapsed time and terminal state. |
| Human interventions | Count each prompt or clarification, manual code edit action, and manually run command within the timed window. Count a continuous edit to one file as one action. For B, the initial slash command starts the workflow and any human action after it counts. Log what happened and when. |
| First-attempt pass | Record whether the first `dotnet test --configuration Release` invocation passed before any test-driven repair. Record build or test failures and distinguish a failed invocation from a run that stopped before testing. |
| Repair cycles | Count each repair followed by a rerun after a failed build or test, using the B run log for autonomous cycles and the A notes for manual cycles. Record which kind of cycle occurred and the last test totals. |
| Code quality | Review each PR against the checklist below. Attach the completed checklist and link to the PR or relevant test evidence. |

### Shared code review checklist

Use **pass**, **fail**, or **not applicable** with a short reason for every item.

- [ ] `dotnet format --verify-no-changes` passes against the repository's `.editorconfig`. Record any pre-existing diagnostics with evidence from the corresponding base.
- [ ] DTO, data, and API changes respect the existing layer boundaries. Persistence and multi-instance behavior are preserved.
- [ ] Error responses follow the API's ProblemDetails and validation conventions and implement the issue's documented status codes.
- [ ] Tests cover the acceptance criteria, including applicable happy paths, validation and failure cases, persistence and multi-instance behavior. The existing Release suite still passes.

## Results

Fill one row per run. Link the timestamped event log or recording, the first and final test outputs, and the completed review. Put any screenshots under `docs/img/`.

| Case | Approach | Tools and model | Start/end UTC and elapsed time | Human interventions | First attempt | Repair cycles | Final tests (passed/failed/skipped) | PR and result | Review and evidence |
| --- | --- | --- | --- | ---: | --- | --- | --- | --- | --- |
| Historical | A — Copilot | — | — | — | — | — | — | — | — |
| Historical | B — /issue-to-pr | — | — | — | — | — | — | — | — |
| Synthetic | A — Copilot | — | — | — | — | — | — | — | — |
| Synthetic | B — /issue-to-pr | — | — | — | — | — | — | — | — |

### Failure and edge-case log

| Run and UTC time | Stage | Observation and impact | Resolution or stop reason | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
