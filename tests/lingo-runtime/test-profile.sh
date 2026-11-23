#!/usr/bin/env bash
set -euo pipefail
probe=$1
assembly=$2
root=$3
mkdir -- "$root"
mkdir -- "$root/profile"
printf 'existing profile sentinel\n' > "$root/profile/lines-spanish.txt"
printf 'not a directory\n' > "$root/invalid-tmp"
state() { sha256sum "$root/profile/lines-spanish.txt" "$root/invalid-tmp"; ls -A -- "$root/profile"; }
before=$(state)
rc=0
"$probe" "$assembly" "$root/profile" > "$root/old-args.log" 2>&1 || rc=$?
[[ $rc == 2 ]]
grep -Fx 'System.ArgumentException: Pass only the built Capri.dll path; the probe creates its own private profile directory under the system temp.' "$root/old-args.log"
rc=0
TMPDIR="$root/invalid-tmp" "$probe" "$assembly" > "$root/bad-tmp.log" 2>&1 || rc=$?
[[ $rc == 2 ]]
grep -F 'The probe could not create its own private profile directory under ' "$root/bad-tmp.log"
! grep -F 'INFO private profile' "$root/old-args.log" "$root/bad-tmp.log"
[[ $(state) == "$before" ]]
printf 'PASS old profile argument and invalid TMPDIR refuse to start without mutating either file\n'
