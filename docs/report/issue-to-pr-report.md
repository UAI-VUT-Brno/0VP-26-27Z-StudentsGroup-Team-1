# From GitHub issue to verified pull request

**Technical and benchmark report · 0VP Team 1 · 18 November 2026**

**Draft.** The workflow analysis and benchmark preflight are complete. The four timed trials and team review are pending.

## 1. Architecture

`/issue-to-pr` is a Claude Code skill that reads an open GitHub issue, delegates the implementation and verification, and opens a pull request against a named base branch. The orchestrator controls stage order, passes file paths between agents, and decides when the run can proceed. The workflow revision for this report is `ecaf1bf`.

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
| Implementer | Reads and edits application code and tests. Uses Bash for build, formatting, and scoped commits. |
| QA and self-healing | Reads diagnostics and edits fixes. Runs the Release suite, records verification metrics, and commits repairs. |
| Release | Edits docs and release artifacts. Uses Git and GitHub CLI to check the diff, push the feature branch, and open the PR. |

The API controller depends on `ITodoRepository`. The DTO layer holds the public data contracts, and the repository implementations handle in-memory and JSON-file storage. Changes that affect persistence must work through both implementations. The planner traces affected callers so a contract change can be built and tested through the normal API entry points. [Pinned workflow source](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/tree/ecaf1bf582858d40d7820c14ec37718b0e130d50/.claude)

## 2. Prompt engineering and failure handling

**Grounding.** The skill loads repository-specific rules for architecture, C# style, tests, concurrency, and repository conventions. The planning prompt requires existing class, method, and test names for each acceptance criterion. It asks the planner to trace both repository implementations and their callers, prefer the smallest design that satisfies the issue, and state decisions where the requirements leave room for interpretation.

**Structured hand-off.** The orchestrator snapshots the GitHub issue into `.issue-to-pr/<n>/issue.json`. The planner returns Markdown with the goal, criteria, implementation by layer, test mappings, required documentation, and risks. The orchestrator saves that output as `plan.md`. Implementation and QA use the same plan. The orchestrator records verification and deviations in `run.md`, which the release agent uses to prepare the PR body. Persisted stage outputs give reviewers a way to trace where an assumption entered the workflow.

**Repair reflection.** QA quotes the diagnostic or failing assertion, locates it in the code, classifies it, identifies the cause, and selects a minimal fix before making an edit. It checks for secondary compiler errors, such as callers failing after a signature change. Existing tests serve as regression checks. The repair prompt prohibits weakening assertions, skipping tests, extending timeouts to hide failures, or suppressing analyzer diagnostics. A new test that contradicts the plan can be corrected in a separate test commit.

**Iteration ceiling.** The first invocation of the Release test suite is attempt one. Three further attempts allow at most three repair cycles. The repair prompt also calls for escalating an unresolved diagnosis in its output after two distinct fixes leave the same failure. If verification remains red, the release stage is designed to open a draft PR with the remaining failures. A run that stops before verification has no first-attempt outcome.

**Guardrails.** Preflight stops on an invalid invocation, a dirty working tree, unavailable GitHub authentication, or an out-of-date base that cannot fast-forward. The workflow also stops for a missing or closed issue or a branch-name collision. The project permission allowlist covers the expected Git, GitHub CLI, and .NET commands, plus edits to the named project paths. It denies destructive Git commands, PR merges, and issue edits. Shell-capable roles share the project allowlist. Their instructions further constrain command and file scope, so a role prompt alone is an instruction boundary. [Workflow settings and prompts](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/tree/ecaf1bf582858d40d7820c14ec37718b0e130d50/.claude)

## 3. Benchmark

The historical fixture adds task ownership across the DTO, data, and API layers, including compatibility for old JSON files. The synthetic fixture adds optimistic concurrency. Its acceptance criteria cover stale writes from separate instances that share a JSON file. Both approaches use identical issue text and a pinned application commit for each case. A fresh clone is used for every trial. The interactive Copilot trial precedes the Claude Code trial on each fixture to avoid exposing the automated solution to the interactive operator.

| Case | Approach | Green PR time | Human input | First pass | Repairs | Quality and evidence |
| --- | --- | --- | --- | --- | --- | --- |
| [#53 Historical](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/53) | Copilot | Pending | Pending | Pending | Pending | Preflight only |
| #53 Historical | Claude Code | Pending | Pending | Pending | Pending | Preflight only |
| [#54 Synthetic](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/54) | Copilot | Pending | Pending | Pending | Pending | Preflight only |
| #54 Synthetic | Claude Code | Pending | Pending | Pending | Pending | Preflight only |

- **Time to a green PR** starts when the runner opens the issue and ends when the PR exists and the submitted code passes the Release suite. A stopped run needs an elapsed time and terminal stage.
- **Human interventions** count prompts, clarifications, manual edits, and manually run commands. For Claude Code, the initial invocation starts the workflow. Additional human actions belong in the event log.
- **First-attempt pass** captures the first Release test invocation before any test-driven repair. Baseline checks and runs that never reach testing have no value for this metric.
- **Repair cycles** count a repair followed by a rerun after a failure. Copilot's interactive repairs and Claude Code's automated cycles should be identified separately when interpreting the counts.
- **Code quality** uses the same checklist for every PR. It checks formatting, layer boundaries, ProblemDetails and validation, acceptance coverage, and the existing Release suite.

The preflight verified fresh clones, the GitHub issue state, and the pinned Claude plugin manifest. The Release suites passed on both starting commits. The four trials still need PR links, timed results, repair counts, and code-quality reviews. [Benchmark protocol and evidence](../benchmark.md)

## 4. Edge cases, failure modes, and model limitations

**Observed access failure.** VS Code opened the historical clone in Restricted Mode and displayed a Copilot sign-in prompt. Claude Code reported no authenticated account and suppressed the project plugin in both untrusted clones. The standalone manifest validator found the skill and all four agents. Loading them in an authenticated, trusted session remains a separate preflight check. Preflight stopped before timing began. [Benchmark preflight log](../benchmark.md#failure-and-edge-case-log)

**Earlier workflow evidence.** The team's task-count pilot reached a PR. The upstream workflow PR records that an intermediate commit broke a caller and that the plan expanded the change into repository code. The planner in the pinned revision now asks for all affected callers to be listed and for the smallest viable design. The benchmark review should check whether those instructions improve the commit sequence and the scope of the submitted change. [Upstream workflow PR](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/52) · [Pilot PR](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/pull/51)

**Case-specific risks.** The historical issue leaves owner mutability as a proposal and includes backward compatibility for records without `userId`. Each PR should state how it handled that ambiguity and demonstrate old-file loading. In the synthetic case, moving the version check outside the repository write could let both API instances act on the same old version. A test against a shared JSON file should prove that a prior successful write makes the other instance's version stale.

**Interpretation limits.** The comparison evaluates two workflows. It combines differences in model, tool access, instructions, and operator input. The Claude workflow's concurrency rule gives specific guidance on where to enforce the version check, which may influence the synthetic result. Two fixtures and one run per approach per fixture will show what happened in those runs. The evidence will be too small to estimate typical performance or rank the underlying models.

**Completion evidence.** Each row needs a PR, verification output tied to the submitted code, a review under the shared checklist, and timestamped run evidence. At least one Claude Code trial needs a complete transcript or recording showing it reached the PR without human input. The report requires team review before submission. [Report assignment](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1/issues/49)
