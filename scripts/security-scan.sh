#!/usr/bin/env bash
# Lokální bezpečnostní kontrola – stejné kontroly, jaké běží v GitHub Actions (security.yml).
# Spuštění z kořene repozitáře:  ./scripts/security-scan.sh
# Na Windows v Git Bash nebo WSL. Při jakémkoli nálezu skončí nenulovým kódem.
set -uo pipefail

cd "$(dirname "$0")/.."
SOLUTION=UAI.ZeroVP.sln
failed=0

step() { printf '\n==> %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; failed=1; }

# 1) NuGet Audit (Directory.Build.props) – zranitelnost středně závažná a horší zastaví restore.
step "Restore + NuGet Audit"
dotnet restore "$SOLUTION" || fail "restore failed (vulnerable package?)"

# 2) Build s bezpečnostními analyzátory .NET (.editorconfig: category-Security = error).
step "Build + .NET security analyzers"
dotnet build "$SOLUTION" --configuration Release --no-restore || fail "build failed (security analyzer finding?)"

# 3) Výpis zranitelných balíčků včetně tranzitivních a nízké závažnosti.
step "Vulnerable NuGet packages"
report=$(dotnet list "$SOLUTION" package --vulnerable --include-transitive 2>&1) || fail "dotnet list package failed"
echo "$report"
if grep -q "has the following vulnerable packages" <<<"$report"; then
    fail "vulnerable packages found"
fi

# 4) Tajné údaje (hesla, tokeny, klíče) v historii gitu i v pracovním adresáři –
#    "dir" zachytí i soubory, které ještě nejsou v gitu.
step "Secrets (gitleaks)"
if command -v gitleaks >/dev/null 2>&1; then
    gitleaks git --no-banner --redact . || fail "secrets found in git history"
    gitleaks dir --no-banner --redact . || fail "secrets found in working tree"
else
    fail "gitleaks is not installed (macOS: brew install gitleaks, Windows: winget install gitleaks)"
fi

echo
if [ "$failed" -ne 0 ]; then
    echo "Security scan FAILED."
    exit 1
fi
echo "Security scan passed."
