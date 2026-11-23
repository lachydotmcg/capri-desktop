#!/usr/bin/env bash
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
d=$(mktemp -d)
trap 'rm -rf -- "$d"' EXIT
mkdir -p "$d/--root/cross"
cp "$repo"/cross/{Mainframe,MainframeSearch,MainframeTheme,MainframePlugins}.cs "$d/--root/cross/"
cd -- "$d"
rc=0
out=$(dotnet run --project "$repo/tests/coverage-pins/CoveragePins.csproj" -c Release -- --root --root "$d/missing" 2>&1) || rc=$?
printf '%s\n' "$out"
(( rc == 1 ))
grep -Fq -- '--root' <<<"$out"
grep -Fxq 'failures=1' <<<"$out"
