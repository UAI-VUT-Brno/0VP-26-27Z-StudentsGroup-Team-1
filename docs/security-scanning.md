# Security scanning

Security checks run in three places: in every `dotnet build`, in a local script, and in the GitHub Actions workflow `.github/workflows/security.yml`. Any finding fails the build or the workflow.

## What is checked

| Check | Tool | Local build | Local script | GitHub Actions |
|---|---|---|---|---|
| Vulnerable NuGet packages (direct and transitive) | NuGet Audit | yes | yes | yes (`CI` and `Security`) |
| Vulnerable NuGet packages, including low severity | `dotnet list package --vulnerable` | – | yes | yes |
| Insecure code patterns (weak crypto, SQL injection, insecure deserialization, …) | .NET security analyzers | yes | yes | yes (`CI` build) |
| Secrets (passwords, tokens, keys) | gitleaks | – | yes | yes |
| Static analysis (SAST) | CodeQL, `security-extended` queries | – | – | yes |
| New vulnerable dependencies in a pull request | Dependency review | – | – | yes (pull requests only) |
| Outdated dependencies | Dependabot | – | – | weekly pull requests |

### In every build

- **NuGet Audit** is enabled for all projects in `Directory.Build.props`. During `dotnet restore` NuGet compares all packages, including transitive ones, with the GitHub Advisory Database. A vulnerability with moderate, high or critical severity (`NU1902`–`NU1904`) is an error and stops the restore. A low-severity one (`NU1901`) stays a warning.
- **.NET security analyzers**: `.editorconfig` sets `dotnet_analyzer_diagnostic.category-Security.severity = error`. This turns on every rule in the Security category (CA2xxx, CA3xxx, CA5xxx) as a build error, for example `CA5351` for MD5.

Because both live in the build itself, the existing `CI` workflow enforces them too. No change to `ci.yml` was needed.

> Do not add `<AnalysisModeSecurity>` to the project files. It sets the severity per rule and overrides the category setting in `.editorconfig` back to warnings.

### GitHub Actions (`security.yml`)

The workflow runs on pushes to `main`, `feature/**` and `fix/**`, on every pull request, every Monday at 05:00 UTC (new advisories appear even without code changes), and manually via *Run workflow*.

| Job | What it does |
|---|---|
| `Vulnerable NuGet packages` | Restores with NuGet Audit and fails if `dotnet list package --vulnerable --include-transitive` reports anything. |
| `Secrets (gitleaks)` | Downloads a pinned gitleaks release, verifies its SHA-256 and scans the full git history. The CLI is used instead of `gitleaks-action` because the action needs a paid licence for organisation repositories. |
| `CodeQL (C#)` | Builds the solution and runs CodeQL with the `security-extended` query suite. Results appear under **Security → Code scanning**. |
| `Dependency review` | On pull requests only: fails if the PR adds or changes a dependency with a moderate or higher vulnerability, and comments a summary on failure. |

Dependabot (`.github/dependabot.yml`) checks NuGet packages and GitHub Actions weekly and opens update pull requests. Microsoft, Serilog and test packages are grouped into one PR each.

To make the checks a real merge gate, a repository admin adds them as required status checks in the branch protection rule for `main`.

## Running the checks locally

Install gitleaks once:

```bash
brew install gitleaks        # macOS
winget install gitleaks      # Windows
```

Then run from the repository root (on Windows use Git Bash or WSL):

```bash
./scripts/security-scan.sh
```

The script runs, in order:

1. `dotnet restore` with NuGet Audit,
2. `dotnet build` with the security analyzers,
3. `dotnet list package --vulnerable --include-transitive`,
4. `gitleaks git` over the git history and `gitleaks dir` over the working tree, so files that are not committed yet are checked too.

It prints `Security scan passed.` and exits with `0`, or prints `FAIL: …` for each failed step and exits with `1`. Run it before pushing.

## Handling a finding

- **Vulnerable package**: update it to a fixed version (the advisory link in the message names it). For a transitive package, add a direct `PackageReference` with the fixed version to the project that pulls it in.
- **Security analyzer error**: fix the code. If the finding is a false positive, suppress that one rule with `#pragma warning disable CAxxxx` and a comment explaining why.
- **Secret**: treat it as leaked. Revoke or rotate it first, then remove it from the code. Removing it in a new commit does not remove it from the history. If it is a false positive, add its fingerprint to `.gitleaksignore`.
- **CodeQL alert**: fix the code, or dismiss the alert in the Security tab with a reason.
