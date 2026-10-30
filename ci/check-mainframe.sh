#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
evidence=${1:?Supply a new absolute directory for private UI evidence}
dotnet restore "$root/tests/mainframe/MainframeChecks.csproj" --locked-mode
dotnet build "$root/tests/mainframe/MainframeChecks.csproj" -c Release --no-restore
python3 "$root/tests/mainframe/run-private.py" "$evidence"
