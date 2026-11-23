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
| Historical | A — Copilot | VS Code 1.147.0, Copilot Free, Chat (Ask), Copilot Fusion → `gpt-6-luna` | 2026-11-23 07:46:17–08:31:11, 44 min 54 s | ≥ 33 | no (build error) | ≥ 3 | 258 / 39 / 2 | [#58](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/58), draft, stopped by the runner, CI fails | [run record](benchmark/runs/53-A.md) |
| Historical | B — /issue-to-pr | Claude Code 2.1.330, `claude-opus-5-5` (effort xhigh), permission mode default, pack `fbff347` | 2026-11-23 08:42:05–09:06:22, 24 min 17 s | 0 | yes | 0 | 345 / 0 / 2 | [#59](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/59), ready, CI green | [run record](benchmark/runs/53-B.md), [transcript](benchmark/runs/53-B/transcript/) |
| Synthetic | A — Copilot | VS Code 1.147.0, Copilot Free, Chat (Ask), Copilot Fusion → `gpt-6-luna` | 2026-11-23 09:19:52–09:47:33, 27 min 41 s | ≥ 18 | no (build error) | 0 | did not build | [#60](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/60), draft, stopped by the runner, CI fails | [run record](benchmark/runs/54-A.md) |
| Synthetic | B — /issue-to-pr | Claude Code 2.1.330, `claude-opus-5-5` (effort xhigh), permission mode default, pack `fbff347` | 2026-11-23 09:52:33–10:33:21, 40 min 48 s | 0 until the PR existed, then 1 (`continue` after the plan's usage limit) | no | 1 | 393 / 0 / 2 | [#61](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/61), ready, build-and-test green, CodeQL: 1 high alert (pre-existing unauthenticated `Delete`, flagged again) | [run record](benchmark/runs/54-B.md), [transcript](benchmark/runs/54-B/transcript/) |

The runs followed the protocol at commit `36a8634` (PR #57, not merged yet at run time). No run was screen-recorded: A runs kept timestamped notes, and their counts are lower bounds reconstructed from the Copilot chat export, the terminal history and the final diff; B runs are documented by their session transcripts. No screenshots were taken; the transcripts and chat exports replace them. Both A runs were stopped by the runner without green tests, and their PRs are drafts. The run PRs stay open as evidence and must not be merged.

**Elapsed time compares different end states.** For A it is the time to a draft PR that is not green (stopped by the runner); for B it is the time to a PR whose Release suite passed. For example, 54-A's 27 min is not faster than 54-B's 40 min: 54-A ended without a building solution.

### Limitations

- **The conditions were not symmetric.** A used Copilot Free in Ask mode with an auto-selected model (`gpt-6-luna`). Ask mode cannot edit files, which explains most of A's interventions. B used `claude-opus-5-5` at effort `xhigh` on a Claude Pro plan.
- **The protocol had no time box or stop rule.** 53-A was stopped after 45 minutes with 39 failing tests, 54-A after its first failed build (0 repair cycles). The A outcomes are therefore partly the runner's decision, not only the tool's result.
- **The pack had hints for both cases.** Its rules were written after `main` had `userId` and optimistic concurrency was planned. `rules/concurrency.md` describes most of the #54 solution, and the 54-B plan cites it. The Copilot runs had nothing comparable.
- **One run per cell.** No B run was repeated, so the variance between runs is unknown.

### Code review

Same checklist for all four PRs. `dotnet format --verify-no-changes` is compared with the baseline of the matching base (55 findings on `base-t1-10`, 101 on `base-synthetic`), so only findings a run adds count against it.

| Item | #58 (53 A) | #59 (53 B) | #60 (54 A) | #61 (54 B) |
| --- | --- | --- | --- | --- |
| Format vs. baseline | pass: 55, no new findings | pass: 28, fixes whitespace in touched files | pass: 101, no new findings | pass: 30, fixes whitespace in touched files |
| Layering, persistence, multi-instance | pass: DTO, Data and API changes in their layers; legacy JSON without `userId` loads | pass: one commit per layer; legacy file test survives a write | fail: version check moved into the repository as designed, but a caller was not updated and the solution does not build | pass: version check and write inside the JSON file lock; multi-instance integration test |
| Errors (ProblemDetails, status codes) | pass: `400` validation for `userId`, but existing tests were not updated to send it | pass: `400` validation for `userId` | not applicable: does not build | pass: `409` ProblemDetails for stale writes, `400` for missing or invalid versions |
| Tests cover the acceptance criteria; suite passes | fail: 39 failures, docs not updated | pass: 345/347, docs and OpenAPI updated | fail: no test ran | pass: 393/395, docs and OpenAPI updated |

### Failure and edge-case log

| Run and UTC time | Stage | Observation and impact | Resolution or stop reason | Evidence |
| --- | --- | --- | --- | --- |
| 53 A, 07:47 | prompt 1 | Copilot fetched the repository's issue search for `userId`, whose results list PR #29 (the original solution). It did not open it, but it is a leakage path for the historical case. | Logged as a threat to validity | `53-A` chat export |
| 53 A, 07:50–08:08 | edits | Ask mode returns fragments without placement; Apply in Editor merged a `Clone()` fragment into `TodoItemDto.cs` and caused a compile error. Several of the twelve prompts only asked where code belongs. | Fixed by hand | [53-A](benchmark/runs/53-A.md) |
| 53 A, 08:10–08:29 | test | Making `userId` required broke about 40 existing tests whose fixtures send no owner. Copilot described the fixture changes in prose across 10+ files. | Runner stopped at 39 failures | [53-A](benchmark/runs/53-A.md) |
| 53 A, 08:05 | test | Copilot suggested a filtered Debug `dotnet test`; the first-attempt metric therefore comes from a filtered run. | Logged | [53-A](benchmark/runs/53-A.md) |
| 53 B, whole run | plan | Threat to validity: the pack's rules and prompts were written after `main` had `userId` and mention it (`csharp-style.md`, `testing.md`, `repair-diagnostics.md`). | Logged; pack used unchanged as pinned | [53-B](benchmark/runs/53-B.md) |
| 53 B, 08:58 | implement | The implementer read a file that does not exist (`DataServiceCollectionExtensions.cs`). | Recovered by itself | 53-B transcript |
| 53 B, 54 B | implement | `dotnet format whitespace` on touched files also fixed pre-existing whitespace, which adds diff noise. | Accepted | PR #59, #61 |
| 54 A, 09:31–09:46 | implement | Copilot's design changed the `ITodoRepository` signatures; every caller and test double had to be updated by hand. One missed call (CS1503) broke the build; new types had no XML docs (CS1591). | Runner stopped at the first failed build | [54-A](benchmark/runs/54-A.md) |
| 54 B, 10:25–10:28 | verify | First test run failed: new tests expected `application/problem+json`, the controllers return `application/json`. QA fixed the tests it had added and kept every assertion. | 1 repair cycle, then green | [54-B](benchmark/runs/54-B.md) |
| 54 B, whole run | plan | Threat to validity, stronger than for 53 B: `rules/concurrency.md` spells out most of the #54 solution (`version` in the PUT body and the DELETE query, `409` ProblemDetails, the check inside the repository write, old files loading as version 1, and the test list). The 54-B plan cites it directly. | Logged; pack used unchanged as pinned | [54-B](benchmark/runs/54-B.md), `54-B/run/plan.md` |
| 54 B, CI of #61 | release | CodeQL reports 1 new high alert, *Missing function level access control* on `TodosController.Delete`. The action existed before; it counts as new because its signature gained the `version` query parameter. The API has no authentication by design, so it is effectively a false positive. #60's CodeQL also fails, because it does not build. | Logged; not fixed in the run PR | PR #61 checks |
| 54 B, 10:33–12:00 | summary | The Claude Pro 5-hour usage limit ran out after the PR existed; the session paused until the runner typed `continue`. | 1 intervention, outside the timed window | 54-B transcript |
| all runs | setup | Claude Code updated itself from 2.1.326 to 2.1.330 between creating the 53 B clone and the run; both B runs used 2.1.330. The notes window (`Read-Host`) could not take pasted text. | Versions recorded from the transcripts | run records |
