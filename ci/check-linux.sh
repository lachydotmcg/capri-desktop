#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
unset WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE DISPLAY DBUS_SESSION_BUS_ADDRESS
for project in tests/desktop/Capri.Desktop.Tests.csproj \
               tests/computer/ComputerChecks.csproj tests/voice/VoiceChecks.csproj \
               tests/brain/BrainChecks.csproj tests/chat-placement/ChatPlacementChecks.csproj; do
    dotnet restore "$root/$project" --locked-mode
    dotnet run --project "$root/$project" -c Release --no-restore
done
make -C "$root/cross/native"
python3 "$root/tests/computer/native_input_checks.py" "$root/cross/native/capri-input"
