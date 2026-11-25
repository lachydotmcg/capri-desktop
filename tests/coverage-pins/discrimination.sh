#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.."
unset WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE DISPLAY DBUS_SESSION_BUS_ADDRESS
d=$(mktemp -d)
trap 'rm -rf -- "$d"' EXIT
mkdir "$d/app" "$d/cross"
cp app/MainframeSearch.cs "$d/app/"
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
reject_pin "the key 'quiet'"
cp cross/MainframeSearch.cs "$d/cross/"
sed -i 's/Slider("Volume", Pet.VolumeNames,/Slider("VOLUME", Pet.VolumeNames,/' "$d/cross/Mainframe.cs"
reject_pin "the search alias 'Volume'"
cp cross/Mainframe.cs "$d/cross/"

# One shared key worded differently on the cross side only fails, naming both files.
sed -i '/Also(index, "Volume",/s/, "loud"/, "noisy"/' "$d/cross/MainframeSearch.cs"
reject_pin "the key 'loud'"
cp cross/MainframeSearch.cs "$d/cross/"

# The same drift from the Windows side fails too, so the pin catches it either way.
sed -i '/Also(index, volume,/s/, "loud"/, "noisy"/' "$d/app/MainframeSearch.cs"
reject_pin "the key 'noisy'"
cp app/MainframeSearch.cs "$d/app/"

# A key only the cross side gains still has to be recorded in the shipped snapshot.
sed -i '/Also(index, "Volume",/s/, "quieter"/, "quieter", "noisy"/' "$d/cross/MainframeSearch.cs"
reject_pin "the search key 'noisy' for 'Volume' is new"
cp cross/MainframeSearch.cs "$d/cross/"

rc=0
out=$(run --root 2>&1) || rc=$?
printf '%s\n' "$out"
(( rc != 0 ))
grep -Fq -- '--root' <<<"$out"
