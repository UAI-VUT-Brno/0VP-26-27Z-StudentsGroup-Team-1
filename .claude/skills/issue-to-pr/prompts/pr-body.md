# Prompt: pull request title and body

Used by: `release`. Input: `issue.json`, `plan.md`, `run.md`, `git log <base>..HEAD`, `git diff <base>...HEAD --stat`. Output: `.issue-to-pr/<n>/pr-body.md` and a title.

## Technique

- Write for a **reviewer** who has not seen the issue or the run. They need to know three things:
  - what changed;
  - why it is safe;
  - how it was verified.
- Describe **behaviour**, not files: "`GET /api/todos/count` returns the number of tasks" rather than "changed TodosController.cs".
- Take every number from `run.md`. Never estimate or round test counts.
- State deviations from the plan, and any changed existing test, explicitly. A reviewer must not discover them in the diff.

## Title

- When the issue title starts with `0VP-T1-NN`: `[0VP-T1-NN] <rest of the issue title>`.
- Otherwise: `<type>: <issue title>`, where `<type>` is `feat` or `fix`.

## Body template

```markdown
## Summary
<2–4 sentences: what the PR does and why, in terms of behaviour>

## Changes
- **DTO:** <…, or "none">
- **Data:** <…>
- **API:** <…>
- **Tests:** <new test classes or methods, and which acceptance criteria they cover>
- **Docs:** <files updated, or "none">

## Verification
| Check | Result |
|---|---|
| `dotnet test --configuration Release` | <passed>/<total> passed, <failed> failed, <skipped> skipped |
| First attempt passed | <yes/no> |
| Repair cycles | <k> |
| `git diff --check` | clean |

<only if any: "### Deviations from the plan" with a list>
<only if verification failed: "### Remaining failures" with the diagnoses from run.md>

Closes #<n>
```

## Self-check

- [ ] Every number in *Verification* matches `run.md`.
- [ ] `Closes #<n>` is the last line, with the correct issue number.
- [ ] No section is empty. Write "none" instead.
- [ ] Nothing in the body claims a check that was not run.
