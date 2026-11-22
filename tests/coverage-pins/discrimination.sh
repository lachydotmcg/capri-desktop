#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.."
unset WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE DISPLAY DBUS_SESSION_BUS_ADDRESS
d=$(mktemp -d)
trap 'rm -rf -- "$d"' EXIT
mkdir "$d/cross"
cp cross/{Mainframe,MainframeSearch,MainframeTheme,MainframePlugins}.cs "$d/cross/"

run() { dotnet run --project tests/coverage-pins/CoveragePins.csproj -c Release -- "$@"; }
reject_pin() {
    local out rc=0
    out=$(run --root "$d" 2>&1) || rc=$?
    printf '%s\n' "$out"
    (( rc == 1 ))
    grep -Fq -- "$1" <<<"$out"
    grep -Fxq 'failures=1' <<<"$out"
}

run --root "$d"
sed -i '/Also(index, "Volume",/s/, "quiet"//' "$d/cross/MainframeSearch.cs"
reject_pin "the search key 'quiet' for 'Volume' is gone"
cp cross/MainframeSearch.cs "$d/cross/"
sed -i 's/Slider("Volume", Pet.VolumeNames,/Slider("VOLUME", Pet.VolumeNames,/' "$d/cross/Mainframe.cs"
reject_pin "the search alias 'Volume'"

rc=0
out=$(run --root 2>&1) || rc=$?
printf '%s\n' "$out"
(( rc != 0 ))
grep -Fq -- '--root' <<<"$out"
