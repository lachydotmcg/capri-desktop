#!/usr/bin/env bash
set -euo pipefail

usage="usage: ci/smoke-gate.sh <report-path> <min-pass> <app-exit-code>"
report=${1:?$usage}
min_pass=${2:?$usage}
app_exit=${3:?$usage}
completion='PASS a system quit with her Mainframe and a sheet open closes every window and ends her'

case $min_pass in
    ''|*[!0-9]*)
        echo "smoke gate: min-pass must be a non-negative integer, got $min_pass" >&2
        exit 2
        ;;
esac

case $app_exit in
    ''|*[!0-9]*)
        echo "smoke gate: app-exit-code must be a non-negative integer, got $app_exit" >&2
        exit 2
        ;;
esac

if ! cat -- "$report"; then
    echo "smoke gate: no report at $report" >&2
    exit 1
fi

if [ "$app_exit" -ne 0 ]; then
    echo "smoke gate: the app exited with status $app_exit" >&2
    exit 1
fi

if grep -q -- '^FAIL' "$report"; then
    echo "smoke gate: the report has a FAIL line" >&2
    exit 1
fi

if [ "$(tail -n 1 -- "$report")" != "$completion" ]; then
    echo "smoke gate: the report is missing the system quit completion line" >&2
    exit 1
fi

passes=$(grep -c -- '^PASS' "$report" || true)
if [ "$passes" -lt "$min_pass" ]; then
    echo "smoke gate: $passes PASS lines is below the $min_pass threshold" >&2
    exit 1
fi

echo "smoke gate: $passes PASS lines, at least $min_pass required" >&2
