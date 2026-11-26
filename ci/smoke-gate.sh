#!/usr/bin/env bash
set -euo pipefail

usage="usage: ci/smoke-gate.sh <report-path> <min-pass> <app-exit-code>"
report=${1:?$usage}
min_pass=${2:?$usage}
app_exit=${3:?$usage}
completion='PASS a system quit with her Mainframe and a sheet open closes every window and ends her'

decimal() {
    case $1 in
        ''|*[!0-9]*) return 1 ;;
    esac
    [ "$1" -ge 0 ] 2>/dev/null
}

if ! decimal "$min_pass"; then
    echo "smoke gate: min-pass must be a non-negative integer, got $min_pass" >&2
    exit 2
fi

if ! decimal "$app_exit"; then
    echo "smoke gate: app-exit-code must be a non-negative integer, got $app_exit" >&2
    exit 2
fi

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
elif [ "$?" -ne 1 ]; then
    echo "smoke gate: could not inspect the report for FAIL lines" >&2
    exit 1
fi

if ! last=$(tail -n 1 -- "$report"); then
    echo "smoke gate: could not read the last report line" >&2
    exit 1
fi
if [ "$last" != "$completion" ]; then
    echo "smoke gate: the report is missing the system quit completion line" >&2
    exit 1
fi

if ! passes=$(grep -c -- '^PASS' "$report") || ! decimal "$passes"; then
    echo "smoke gate: could not count PASS lines" >&2
    exit 1
fi
if [ "$passes" -lt "$min_pass" ]; then
    echo "smoke gate: $passes PASS lines is below the $min_pass threshold" >&2
    exit 1
fi

echo "smoke gate: $passes PASS lines, at least $min_pass required" >&2
