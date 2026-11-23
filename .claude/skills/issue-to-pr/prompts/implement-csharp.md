# Prompt: layered C# implementation

Used by: `implementer`. Input: `plan.md`, `issue.json`, the rules. Output: commits on the current branch.

## Technique

Implement the plan one layer at a time. Finish and commit a layer before you start the next.

```text
DTO  →  Data (+ its tests)  →  API (+ its tests)
```

For each layer:

1. **Read before you write.** Open every file the plan lists for this layer, plus one neighbouring file of the same kind, e.g. another DTO or another controller action. Copy their patterns: XML docs, attributes, naming, error handling.
2. **Write the code.**
   - Follow `architecture.md` and `csharp-style.md`.
   - Comments and XML docs are in Czech. Explain *why*, not *what*.
   - Change only what the plan lists. If the plan is wrong or incomplete, make the smallest correction and note it for your final message.
3. **Write the tests for this layer** (`testing.md`).
   - At least one test for each acceptance criterion the plan maps to this layer.
   - Put a comment above each test naming the criterion: `// AC 2: ...`.
4. **Build.** Run `dotnet build UAI.ZeroVP.sln --configuration Release`.
   - Fix every error and every **new** warning, including callers in other layers that your change broke.
   - Rebuild until it is clean.
5. **Format the changed files.**
   - Run `dotnet format whitespace UAI.ZeroVP.sln --include <changed .cs files>`. This fixes BOM, CRLF, indentation and the final newline.
   - Do not format other files, and do not run the full `dotnet format`: its analyzer fixes can rewrite code.
6. **Commit** (`repository.md`):
   - `git add <paths>`;
   - `git commit -m "<type>(<scope>): <summary>"`.
   - Code and its tests may go in one commit (`feat(data): ...`) or two (`feat(data): ...`, then `test(data): ...`). Either way, **each commit must build**.

## Self-check before you return

- [ ] Every file in the plan is changed, or the deviation is explained.
- [ ] No file outside the plan is changed without an explanation.
- [ ] `dotnet build` is clean, with no new warnings.
- [ ] Every acceptance criterion has a test with an `// AC n` comment.
- [ ] Changed `.cs` files are UTF-8 with BOM and CRLF (done by `dotnet format whitespace --include`).
- [ ] No `TODO`, commented-out code, `Console.WriteLine` or `!` null-forgiving operator was added.

## Output format

```text
commits:
- <hash> <message>
deviations_from_plan:
- <file>: <what and why>, or "none"
```
