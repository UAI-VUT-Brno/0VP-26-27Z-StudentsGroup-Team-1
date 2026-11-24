# From GitHub issue to verified pull request

**Technical and benchmark report | 0VP Team 1 | 23 November 2026**

**Submission status:** technical content and benchmark analysis complete; review by every team member remains pending. This report does not certify that acceptance criterion.

## 1. Architecture

`/issue-to-pr` is a Claude Code skill that reads an open GitHub issue, delegates implementation and verification, and opens a pull request against a named base branch. The orchestrator controls stage order, passes file paths between agents, and decides when the run can proceed. The evaluated workflow revision is `fbff347edad07eea0e17506e923ce5f952f13012`. Its pack contains `SKILL.md`, four agent definitions, prompts, rules and a plugin manifest; project permissions live separately in `.claude/settings.json`.

```text
Issue + base
    v
Orchestrator   Validate -> preflight -> ingest issue.json
    v
Planner        Read issue and code -> return plan -> write plan.md
    v
Orchestrator   Create feature branch from the requested base
    v
Implementer    DTO -> data -> API -> tests -> scoped commits
    v
QA             Release suite -> diagnose -> repair -> rerun (max. 4 attempts)
    v
Release        Sync docs -> check diff -> push -> open PR
    v
Orchestrator   Summarize tests, repair cycles, PR and run log
```

| Role | Tool scope and output |
| --- | --- |
| Orchestrator | Validates issue input and the base, checks Git and GitHub access, fetches the issue, writes hand-off files, and delegates stages. |
| Planner | Read, Grep, and Glob. Maps acceptance criteria to code, tests, and documentation. Returns a plan or a reason the issue cannot proceed. |
| Implementer | Read/search, Edit, Write and Bash; edits only `src/` and `tests/`, builds, formats and commits per layer. No test execution or push. |
| QA and self-healing | Read/search, Edit, Write and Bash; runs Release tests, diagnoses failures, formats and commits repairs. No push. |
| Release | Read/search, Edit, Write and Bash; edits docs and run artifacts, checks the diff, pushes and creates the PR. No application edits or merge. |

The API controller depends on `ITodoRepository`. The DTO layer holds public data contracts; repository implementations handle in-memory and shared JSON-file storage. Persistence changes must work through both implementations. The planner traces affected callers so a contract change can be built and tested through normal API entry points. A new repository signature can otherwise break a controller or test double even when its implementation is correct. [Pinned workflow source](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/tree/fbff347edad07eea0e17506e923ce5f952f13012/.claude)

<!-- pagebreak -->

## 2. Prompt engineering and failure handling

**Grounding.** The skill loads repository-specific rules for architecture, C# style, tests, concurrency, and repository conventions. The planning prompt requires existing class, method, and test names for each acceptance criterion. It asks the planner to trace both repository implementations and their callers, prefer the smallest design that satisfies the issue, and state decisions where the requirements leave room for interpretation.

**Structured hand-off.** The orchestrator snapshots the GitHub issue into `.issue-to-pr/<n>/issue.json`. The planner returns Markdown with the goal, criteria, implementation by layer, test mappings, required documentation, and risks. The orchestrator saves that output as `plan.md`. Implementation and QA use the same plan. The orchestrator records verification and deviations in `run.md`, which the release agent uses to prepare the PR body. Persisted stage outputs give reviewers a way to trace where an assumption entered the workflow.

**Repair reflection.** QA quotes the diagnostic or failing assertion, locates it in the code, classifies it, identifies the cause, and selects a minimal fix before making an edit. It checks for secondary compiler errors, such as callers failing after a signature change. Existing tests serve as regression checks. The repair prompt prohibits weakening assertions, skipping tests, extending timeouts to hide failures, or suppressing analyzer diagnostics. A new test that contradicts the plan can be corrected in a separate test commit.

**Iteration ceiling.** The first invocation of the Release test suite is attempt one. Three further attempts allow at most three repair cycles. If the same failure survives two distinct fixes, QA stops changing that area and reports the diagnosis. It also stops early when no allowed fix remains. If verification remains red, release is instructed to open a draft PR with remaining failures. Neither measured B run exhausted the ceiling, so that fallback is a documented path rather than an observed outcome. A test command that fails during build is a failed invocation; a run stopped before any test invocation has no first-attempt result.

**Guardrails.** Parsing accepts an issue number, `#number`, or a same-repository issue URL, plus an optional base branch. Invalid input stops before Git changes. Preflight requires a clean tree, GitHub authentication, an existing remote base and a successful fast-forward update. Missing/closed issues, an unimplementable plan and branch collisions stop before implementation. Push or PR failures preserve the local branch and report the error.

Project permissions allow expected Git, GitHub CLI and .NET commands and edits to named project paths. They deny force pushes, destructive resets/cleaning, branch deletion, issue edits/closure and PR merges. Shell-capable roles share the project allowlist; their narrower role scopes are prompt instructions rather than separate operating-system sandboxes. The planner's read-only tools provide a stronger boundary. [Workflow settings and prompts](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/tree/fbff347edad07eea0e17506e923ce5f952f13012/.claude)

Rules give existing code precedence if it disagrees with the pack and require the mismatch to be reported. This limits unrelated corrections, but case-specific rules can also leak solution guidance into an evaluation. Persisted hand-offs make that influence reviewable; they do not independently prove the model's reasoning or the correctness of its output.

<!-- pagebreak -->

## 3. Benchmark: method and measured results

The historical case, #53, reproduces task ownership across DTO, Data and API, including compatibility with JSON saved before `userId`. Its base is `benchmark/base-t1-10` at `d562bdae94293fc044bc0353c2651f56d498b443`. Synthetic case #54 adds optimistic concurrency, stale-write errors and shared-file multi-instance tests. Its base is `benchmark/base-synthetic` at `d7466ebe8ff58b26bdf6f0a808d3172d467f0320`. Each trial used an independent clone and identical case text; A preceded B for each case. All runs used Windows 11 and .NET SDK 10.0.403. Baseline checks are setup evidence, separate from measured runs. [Protocol and consolidated evidence](../benchmark.md)

A used VS Code 1.147.0, Copilot Free in Ask mode, and Copilot Fusion auto-selection resolved to `gpt-6-luna`. B used Claude Code 2.1.330, `claude-opus-5-5`, effort `xhigh`, Claude Pro, permission mode `default` and pack `fbff347`. The runs used protocol revision `36a8634` before its PR #57 was merged, a recorded deviation. The B pack was copied into clones and excluded locally with run artifacts. Both cases retained their pinned application baselines.

| Run / PR / evidence | Elapsed to PR | Human input | First pass | Repairs | Final tests P/F/S |
| --- | --- | --- | --- | --- | --- |
| [53-A](../benchmark/runs/53-A.md) / [#58](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/58) | 44m 54s, stopped | >=33 | No* | >=3 manual | 258/39/2 |
| [53-B](../benchmark/runs/53-B.md) / [#59](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/59) | 24m 17s, green | 0 | Yes | 0 | 345/0/2 |
| [54-A](../benchmark/runs/54-A.md) / [#60](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/60) | 27m 41s, stopped | >=18 | No | 0 | Build failed |
| [54-B](../benchmark/runs/54-B.md) / [#61](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/61) | 40m 48s, green | 0 timed; 1 later | No | 1 automated | 393/0/2 |

Every numeric result in this table comes from the linked run's Timing and Metrics sections. P/F/S means passed/failed/skipped; the two skips are existing opt-in performance tests. Each record links underlying chat exports or session/subagent transcripts, run artifacts and the PR. No trial was screen-recorded. A counts are reconstructed lower bounds because the notes window lost entries; the historical terminal history is also incomplete.

*53-A's first invocation was a filtered Debug test command, deviating from the full Release first-pass definition. It failed to build. The final 258/39/2 outcome comes from the later full Release run. Its first-pass result is evidence of that failed invocation, not an equivalent full-suite observation.

**Timing boundary.** The protocol ends timing once both the PR exists and the submitted code passes Release tests. A never reached that endpoint: its elapsed values end at runner-stopped draft PRs. B reached the green-test endpoint; #61 still had a CodeQL alert. For 54-B, the start is command entry because the separate issue-open timestamp was not captured. Times describe distinct end states and do not support a direct speedup calculation.

<!-- pagebreak -->

## 3. Benchmark: interpretation of every metric

**Time to a green PR.** Historical B reached a passing PR in 24m 17s, while A stopped after 44m 54s with failures. Synthetic B took 40m 48s; A's shorter 27m 41s ended with a compile error. Neither A value measures time to green, and no completion time can be estimated from the stopped runs. B's endpoint is local Release verification plus PR creation; security CI is assessed separately.

**Human interventions.** Historical A has at least 33 actions: 12 prompts, at least 13 file edits and at least 8 commands. Synthetic A has at least 18: 3 prompts, at least 10 edits and 5 commands. Ask mode returns suggestions that the operator must apply, so counts partly measure workflow affordances. B had no post-invocation input during either timed window. Historical B also completed its final summary unattended; synthetic B needed one `continue` after PR creation when a usage limit interrupted the summary. That later action remains relevant even though it is outside the timed metric.

**First-attempt pass.** Only historical B passed first time. Synthetic B reached testing but its newly added assertions expected the wrong response media type. Both A first test invocations failed to build, with the historical definition deviation noted above. Four trials do not estimate a reliable pass probability; a build failure must not be presented as a zero-test successful run.

**Repair cycles.** Historical A required at least three manual cycles; synthetic A had none because the operator stopped immediately after the failed build. Zero repairs there says nothing about correctness. Historical B needed none; synthetic B needed one automated repair and rerun. The repair corrected new tests to match existing `application/json` responses, retaining status, detail and validation assertions. It did not change the production contract merely to satisfy new tests.

**Code quality.** The shared review evaluates formatter diagnostics against the matching baseline, layering/persistence, ProblemDetails/status codes, and acceptance coverage with the Release suite. This table transcribes the [consolidated review](../benchmark.md#code-review), which supplies reasoning and PR evidence.

| Review item | #58 / 53-A | #59 / 53-B | #60 / 54-A | #61 / 54-B |
| --- | --- | --- | --- | --- |
| Format vs baseline | Pass: 55 findings | Pass: 28 | Pass: 101 | Pass: 30 |
| Layers / persistence | Pass | Pass | Fail: missed caller | Pass: atomic write |
| Errors / validation | Pass | Pass | N/A: no build | Pass: 400/409 |
| Coverage + suite | Fail: 39 failures | Pass | Fail: no tests ran | Pass |

Baseline formatter counts were 55 historical and 101 synthetic. A format pass thus means no added findings, not a clean formatter exit. B reduced existing whitespace findings in touched files, adding diff noise. #58 still lacked documentation and valid fixtures; #60 left caller updates incomplete. B updated docs/OpenAPI and covered legacy-file behavior; synthetic B tested stale writes across instances. Passing functional tests does not dispose of #61's security alert.

<!-- pagebreak -->

## 4. Edge cases and failure modes

This section derives from the execution log for task 21, implemented by issue #48 and merged PR #62, rather than the earlier preflight-only draft. [Failure log](../benchmark.md#failure-and-edge-case-log)

**Fragment placement and incomplete propagation (53-A).** Ask mode returned fragments whose destinations were unclear. Apply in Editor put a `Clone()` fragment into the DTO file, causing compilation errors; the operator repaired placement manually. Requiring `userId` then invalidated existing fixtures across many files. Further prose instructions did not produce a passing suite before the stop. The practical failure was propagation across all callers and fixtures, not just generating a property. A stronger future protocol should require a caller/fixture inventory and record each applied edit.

**Signature changes and missing callers (54-A).** The suggested concurrency design placed version checks in the repository, consistent with atomic shared-file writes. Changing repository signatures required every controller caller and test double to change. One remaining call passed a cancellation token where the new expected version belonged (CS1503), preventing the build. New result types also produced CS1591 documentation warnings. The runner applied only part of a long test answer, then stopped at the first build failure. A sound design was insufficient without complete integration and verification.

**Incorrect test assumptions and bounded recovery (54-B).** Newly generated tests expected `application/problem+json`, but the controllers' existing contract uses `application/json`. QA diagnosed the mismatch, corrected only new tests, committed the correction separately and reran successfully. This is observed self-repair within one cycle. It does not prove that a model can safely repair arbitrary assertions: the existing contract and plan supplied an external check on the correction.

**Non-blocking discovery errors (53-B).** The implementer tried to read a nonexistent `DataServiceCollectionExtensions.cs` path and recovered autonomously. The branch-existence probe also returned an expected non-zero status. These events distinguish recoverable exploration from terminal verification failures. Documentation work moved from implementation to release as the role scopes intended, and was recorded as a plan deviation.

**Security signal (54-B).** CodeQL reported a high-severity missing function-level access-control alert on `TodosController.Delete`, newly surfaced after its signature gained the version parameter. The benchmark record attributes it to the pre-existing unauthenticated API design. This report records that explanation without treating it as a security clearance: build-and-test was green, but the alert remained unresolved and requires separate review.

**Operational failures.** Claude Code changed version between clone creation and execution; both B transcripts identify 2.1.330. The notes window could not accept pasted text and lost most A events, while a terminal restart lost history in 53-A. Synthetic B exhausted its plan's usage allowance after release and resumed for the summary. Reliable capture, version pinning and an explicit budget policy are prerequisites for a repeatable unattended evaluation. Earlier sign-in/trust blockers were setup observations, not timed failures.

<!-- pagebreak -->

## 4. Model limitations, validity and submission review

**Unequal assistance.** This experiment compares complete workflows, with different models, plans, tool permissions and operator effort. Copilot Free Ask mode could not directly edit the workspace; Claude Pro used a high-effort model and a specialised agent pack. Most A interventions follow from that setup. Results cannot isolate the effect of multi-agent decomposition or establish that one underlying model is better.

**Case-specific prior information.** The pack was authored after task ownership existed on `main`; its rules and prompts mention `userId`. More strongly, `rules/concurrency.md` describes the synthetic version contract, atomic repository write, status codes, legacy-file default and relevant tests. The synthetic B plan cites that rule. A did not receive equivalent guidance. Historical A fetched issue-search results listing the original solution PR #29; it did not open that PR according to the record, but the search is still a leakage path. Future comparisons should use unseen cases and equal grounding material.

**Stopping and measurement.** The protocol defined no common time box or stopping rule. A was stopped at different stages: after repeated ownership failures and after the first concurrency build failure. Missing A notes make intervention and repair counts lower bounds. Historical A used a filtered Debug first attempt, and synthetic B starts timing at command entry rather than a separately recorded issue opening. These deviations limit comparisons and must not be silently normalised.

**Sample size and scope.** There is one trial per case per approach, with no repeated B run. Variance is unknown; no average, percentage speedup or general success rate is justified. The evidence supports a narrower finding: the configured Claude workflow produced two PRs with passing Release suites, including one fully unattended session through the final summary. The interactive Ask-mode trials produced two stopped draft PRs. One B run demonstrated a successful repair; neither tested ceiling exhaustion or failed-release recovery.

For a stronger follow-up, predefine stops and budget limits, freeze tool/model versions, use durable timestamped recording, give both workflows equivalent rules, and repeat unseen cases. Review both passing tests and independent security/contract checks; model-written tests can reproduce the model's own incorrect assumptions.

**Traceability and export.** The four row-level records link raw Copilot exports, command histories, B session/subagent transcripts, issue snapshots, plans, QA results and PR bodies. The consolidated review supplies quality judgments. Regenerate this six-page PDF from the Markdown with `python scripts/export-report.py` using ReportLab. Evidence links in the PDF target pinned merged revision `f026376`; page boundaries, tables and the stage diagram are preserved.

**Team review before submission.** Issue #49 requires review by all team members. No completed team sign-off is present in the evidence checked for this report. Each member must review the Markdown and PDF, verify rows against the linked records, and record their name, date and outcome in the report PR before submission. This is a human acceptance step; technical completion and document checks do not satisfy it. Issue #49 should remain open until review is recorded.

Sources: [report requirements #49](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/49), [assignment #43](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/43), [benchmark execution #62](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/62), [protocol, review and failure log](../benchmark.md), and the four run records linked in the results table.
