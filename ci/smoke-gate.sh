#!/usr/bin/env bash
set -euo pipefail

usage="usage: ci/smoke-gate.sh <report-path> <min-pass> <app-exit-code>"
report=${1:?$usage}
min_pass=${2:?$usage}
app_exit=${3:?$usage}

if [ -f "$report" ]; then
    cat "$report"
fi

if [ "$app_exit" -ne 0 ]; then
    echo "smoke gate: the app exited with status $app_exit"
    exit 1
fi

if [ ! -f "$report" ]; then
    echo "smoke gate: no report at $report"
    exit 1
fi

if grep -q '^FAIL' "$report"; then
    echo "smoke gate: the report has a FAIL line"
    exit 1
fi

if ! grep -qxF 'PASS a system quit with her Mainframe and a sheet open closes every window and ends her' "$report"; then
    echo "smoke gate: the report is missing the system quit completion line"
    exit 1
fi

passes=$(grep -c '^PASS' "$report" || true)
if [ "$passes" -lt "$min_pass" ]; then
    echo "smoke gate: $passes PASS lines is below the $min_pass threshold"
    exit 1
fi

echo "smoke gate: $passes PASS lines, at least $min_pass required"
