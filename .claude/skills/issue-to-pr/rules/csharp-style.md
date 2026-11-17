# C# style rules

## Files and encoding

`.editorconfig` is the source of truth. For every `.cs` file you create or change:

- **UTF-8 with BOM.** Without the BOM, the compiler on Windows reads the file in the ANSI code page and breaks Czech characters.
- **CRLF line endings**, 4-space indentation, a final newline and no trailing whitespace.
- One public type per file. The file name equals the type name.
- File-scoped namespaces that match the folder: `UAI.ZeroVP.Data.JsonFile`, `UAI.ZeroVP.Tests.API`.

`.md` files use LF and 2-space indentation. `.json` and `.yml` files use 2-space indentation.

Check only the files you changed:

```text
dotnet format UAI.ZeroVP.sln --verify-no-changes --include <changed .cs files>
```

The existing code does **not** pass `dotnet format` as a whole. Do not reformat files you did not otherwise change.

## Language

| What | Language |
|---|---|
| Code comments and XML docs (`/// <summary>`) | Czech |
| Validation messages (`ErrorMessage = "..."`) and ProblemDetails `title` / `detail` | Czech |
| Log message templates | the language of the surrounding file (controllers: Czech, `JsonFileTodoStore` and `Program.cs`: English) |
| MCP tool and parameter descriptions (`[Description]`) | English |
| Identifiers | English |
| Documentation in `docs/` and `README.md` | English |

## Code

- `sealed` for classes and records unless they are designed for inheritance.
- DTOs are `sealed record` with `init` properties. Response DTOs mark every property `required`, so OpenAPI lists it as required.
- Nullable reference types are on. Do not use `!` to silence a warning. Fix the cause.
- Guard public method arguments with `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrWhiteSpace`.
- Asynchronous all the way: `async Task`, `CancellationToken cancellationToken = default` as the last parameter of every async method, passed down to every call. Never `.Result` or `.Wait()`.
- Use named arguments when calling repository methods with optional parameters. Example: `GetAllAsync(userId: userId, isCompleted: isCompleted, cancellationToken: cancellationToken)`.
- Use structured logging with placeholders (`"Smazán úkol {TodoId}."`), never string interpolation in a log call.
- Compare strings with an explicit `StringComparison`.
- Every public type and member has an XML doc comment. Explain **why** the code does something, not what it does.

## Build

- The build must have **no new warnings**. Security analyzer rules (`CA2xxx`, `CA3xxx`, `CA5xxx`) are errors (`.editorconfig`).
- Medium and higher NuGet vulnerabilities fail the build (`Directory.Build.props`). Do not add packages unless the issue requires it. If you must, use the latest stable version.
