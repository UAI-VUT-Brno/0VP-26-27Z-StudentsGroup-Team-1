#!/usr/bin/env bash
# Spustí několik instancí API vedle sebe nad společným souborem s úkoly.
# Spuštění z kořene repozitáře:  ./scripts/run-instances.sh [počet]
# Výchozí počet je 3: instance api-5001, api-5002, api-5003 na portech 5001–5003.
# Na Windows v Git Bash nebo WSL. Ctrl+C ukončí všechny instance.
set -euo pipefail

cd "$(dirname "$0")/.."
PROJECT=src/UAI.ZeroVP.API
APP="$PROJECT/bin/Debug/net10.0/UAI.ZeroVP.API.dll"
count="${1:-3}"

dotnet build "$PROJECT" --nologo --verbosity quiet

pids=()
stop_all() {
    kill "${pids[@]}" 2>/dev/null || true
    wait 2>/dev/null || true
}
trap stop_all INT TERM EXIT

for i in $(seq 1 "$count"); do
    port=$((5000 + i))
    # Proměnné prostředí platí jen pro tento proces – každá instance má své jméno a port.
    # Úkoly i logy jdou do společných souborů v src/UAI.ZeroVP.API/data a logs.
    # Spouští se přímo sestavená aplikace, ne "dotnet run": ten na Windows po ukončení
    # nechá aplikaci běžet dál jako samostatný proces.
    ASPNETCORE_ENVIRONMENT=Development \
    Instance__Name="api-$port" \
        dotnet "$APP" --contentRoot "$PWD/$PROJECT" --urls "http://localhost:$port" &
    pids+=("$!")
    echo "api-$port: http://localhost:$port/api/info"
done

wait