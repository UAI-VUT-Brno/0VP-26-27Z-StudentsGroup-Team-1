# Prompt: compiler and test diagnostic reflection (repair)

Used by: `qa`. Input: the output of `dotnet test --configuration Release`, `plan.md`, the rules. Output: one fix commit per repair cycle.

## Technique: reflect before you edit

For **each** failure, write a short diagnosis in your own reasoning **before** you touch a file:

```text
Failure:    <exact diagnostic or test name + assertion message, quoted>
Location:   <file:line from the diagnostic or the first stack frame in src/ or tests/>
Kind:       compile | test-new | test-existing | flaky?
Cause:      <one sentence: what in the code makes this fail>
Owner:      production code | new test (contradicts the plan)
Fix:        <the smallest change that removes the cause>
```

Then apply the fixes, commit, and rerun the suite.

## Reading the output

- **Compiler errors** look like `path(line,col): error CS0103: The name 'x' does not exist...`.
  - Fix the **first** error of each file first: later errors are often consequences of it.
  - Common codes:

    | Code | Usual cause | Usual fix |
    |---|---|---|
    | `CS0103` / `CS0246` | missing `using`, typo, type in the wrong layer | add the `using` or move the type, respecting DTO → Data → API |
    | `CS1061` | member missing on a type, often an interface method not added | add it to the interface **and** both repository implementations |
    | `CS7036` / `CS1501` | wrong number of arguments after a signature change | update the caller, or give the new parameter a default value |
    | `CS0535` | interface member not implemented | implement it in the missing class |
    | `CS8618` / `CS86xx` | nullability | make the property `required`, initialise it, or make it nullable as the contract says. Never use `!` |
    | Security category, e.g. `CA2100`, `CA3075`, `CA5350` | security analyzer (an error here) | fix the code. Never suppress the rule |
- **Test failures** look like `Failed <Namespace.Class.Method>` followed by `Assert.Equal() Failure`, `Expected:` and `Actual:`.
  - Read the test first, then the code under test.
  - Ask yourself: does the **production code** break the behaviour the test describes, or does a **new** test assume something the plan does not say?
- **Existing tests** describe behaviour that was correct before this branch. If one fails, the new production code broke something. Fix the production code.
  - A test is **existing** when it is on the base branch, and **new** when this branch added it. Check with `git diff <base>...HEAD --stat`, using the base branch you received, not `main`.

## Hard limits

- Never delete, skip (`Skip =`), comment out or weaken a test. Never change an assertion of an existing test.
- You may change a **new** test (added on this branch) only when it contradicts the plan. Commit it separately as `test(<scope>): correct <test name> to match the plan`, and explain why in your output.
- Never add `Thread.Sleep`, retries, `[Trait]` filters or longer timeouts to make a test pass.
- Never suppress a warning or analyzer rule (`#pragma warning disable`, `[SuppressMessage]`, `NoWarn`).
- If the same failure survives two cycles with two different fixes, stop changing that area. Report it as a remaining failure with your diagnosis.

## Example

```text
Failure:    Failed UAI.ZeroVP.Tests.API.TodoUserFilterEndpointTests.GetAll_UserFilter_WorksWithCompletionFilterAndEdgeCases
            Assert.Empty() Failure: Collection: [{ "userId": "alice", ... }]
Location:   tests/API/TodoUserFilterEndpointTests.cs:62 → InMemoryTodoRepository.cs:88
Kind:       test-existing
Cause:      the new filter compares userId with OrdinalIgnoreCase, but userId is case-sensitive (alice != Alice).
Owner:      production code
Fix:        use StringComparison.Ordinal in the filter.
```
