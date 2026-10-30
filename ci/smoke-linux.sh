#!/usr/bin/env bash
set -euo pipefail

binary=${1:?Usage: ci/smoke-linux.sh /absolute/Capri /absolute/evidence}
evidence=${2:?Usage: ci/smoke-linux.sh /absolute/Capri /absolute/evidence}
if [[ $binary != /* || $evidence != /* || -e $evidence ]]; then
    printf '%s\n' 'Use absolute paths and a new evidence directory.' >&2
    exit 2
fi
mkdir -p -m 700 "$evidence/profile" "$evidence/runtime"
export XDG_CONFIG_HOME="$evidence/profile/config"
export XDG_DATA_HOME="$evidence/profile/data"
export XDG_CACHE_HOME="$evidence/profile/cache"
export XDG_STATE_HOME="$evidence/profile/state"
export XDG_RUNTIME_DIR="$evidence/runtime"
export XDG_CURRENT_DESKTOP=Openbox XDG_SESSION_TYPE=x11
unset WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE DBUS_SESSION_BUS_ADDRESS
export CAPRI_SMOKE_BINARY="$binary" CAPRI_SMOKE_EVIDENCE="$evidence"
export CAPRI_SMOKE_READY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/wait-x11.py"

xvfb-run -a -s '-screen 0 1920x1080x24 -nolisten tcp' dbus-run-session -- bash -euo pipefail -c '
    openbox > "$CAPRI_SMOKE_EVIDENCE/openbox.log" 2>&1 &
    window_manager=$!
    xcompmgr > "$CAPRI_SMOKE_EVIDENCE/compositor.log" 2>&1 &
    compositor=$!
    trap '\''kill "$compositor" "$window_manager" 2>/dev/null || true; wait || true'\'' EXIT
    python3 "$CAPRI_SMOKE_READY"
    timeout 210 "$CAPRI_SMOKE_BINARY" --smoke "$CAPRI_SMOKE_EVIDENCE/checks"
'
test -f "$evidence/checks/smoke-test.txt"
if grep -q '^FAIL' "$evidence/checks/smoke-test.txt"; then
    cat "$evidence/checks/smoke-test.txt"
    exit 1
fi
test "$(grep -c '^PASS' "$evidence/checks/smoke-test.txt")" -ge 65
cat "$evidence/checks/smoke-test.txt"
