# Prompt: requirement decomposition (plan)

Used by: `planner`. Input: `issue.json`, the rules, the repository. Output: one Markdown plan.

## Technique

Work in this order. Do not write the plan until steps 1–4 are done.

1. **Restate the goal** in one sentence, in your own words. If you cannot, the issue is not implementable.
2. **Extract the acceptance criteria.**
   - If the issue has a checklist, copy each item verbatim and number it.
   - If it has none, derive testable criteria from the text. Mark derived ones with `(derived)`.
   - Every criterion must be checkable by an automated test or a doc change.
3. **Ground every change in the code.** For each criterion:
   - find the code it touches with Grep/Glob and read it;
   - name existing types, methods and tests by their real names. A path in the plan must exist, or be a new file in an existing folder that follows its naming.
   - Look for an existing method to reuse before you plan a new one (`architecture.md`, *Scope of a change*).
4. **Choose the smallest design** that meets every criterion. If there are two reasonable designs, pick the one that changes fewer public contracts, and give the reason in one line under *Decisions*.
5. **Write the plan** in the format below.

## Self-check before you return

- [ ] Every acceptance criterion has at least one test in *Tests* (`→ AC n`).
- [ ] Every changed repository method appears in **both** `InMemoryTodoRepository` and `JsonFileTodoRepository`.
- [ ] Every caller of a changed signature is listed in the same layer section as the change, so that each commit builds.
- [ ] No file outside `src/`, `tests/`, `docs/` and `README.md` is listed.
- [ ] *Documentation* follows the table in `repository.md`.

## Output format

Return exactly this Markdown and nothing else:

```markdown
# Plan for #<n>: <title>

## Goal
<one or two sentences>

## Acceptance criteria
1. <criterion>
2. <criterion> (derived)

## Decisions
- <design choice and why>, or "none"

## Changes by layer
### DTO (`src/UAI.ZeroVP.DTO`)
- `<path>`: <what changes and why>
### Data (`src/UAI.ZeroVP.Data`)
- ...
### API (`src/UAI.ZeroVP.API`)
- ...

## Tests (`tests/`)
- `<path>`: `<TestMethodName>` → AC <n>

## Documentation
- `<path>`: <what changes>, or "none"

## Risks
- <compatibility, persistence or concurrency concerns>, or "none"
```

Write "none" under a layer that does not change.

If the issue cannot be implemented inside this repository, return only:

```text
NOT IMPLEMENTABLE: <reason in one sentence>
```

## Example of a good change line

```markdown
- `src/UAI.ZeroVP.API/Controllers/TodosController.cs`: add `Count` action `[HttpGet("count")]` that calls the existing `GetAllAsync(userId:, isCompleted:)` and returns `TodoCountDto`; no repository change needed.
```
