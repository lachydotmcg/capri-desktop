#!/bin/sh
# Builds Capri for Linux into out/linux and installs a launcher for it in your app menu.
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet; else DOTNET="$HOME/.dotnet/dotnet"; export DOTNET_ROOT="$HOME/.dotnet"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# A 64-bit kernel under 32-bit Raspberry Pi OS still says aarch64, so the userland's word size decides.
if [ "$(getconf LONG_BIT)" != 64 ]; then echo "Capri needs a 64-bit system; this one runs 32-bit programs." >&2; exit 2; fi
case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64|arm64) rid=linux-arm64 ;;
    *) echo "Capri builds on 64-bit x86 or arm Linux, not $(uname -m)." >&2; exit 2 ;;
esac
make -C cross/native
"$DOTNET" publish cross/Capri.Cross.csproj -c Release -r "$rid" --self-contained -p:RestoreLockedMode=true -p:NuGetLockFilePath="packages.$rid.lock.json" -o out/linux -p:DebugType=none
python3 ci/copy-linux-notices.py cross/obj/project.assets.json out/linux
install -Dm755 cross/native/capri-input out/linux/native/capri-input
install -Dm644 LICENSE.md out/linux/LICENSE.md
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
icons="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
mkdir -p "$apps" "$icons"
cp cross/icon.png "$icons/capri.png"
sed "s|@EXEC@|$PWD/out/linux/Capri|" linux/capri.desktop > "$apps/capri.desktop"
update-desktop-database "$apps" 2>/dev/null || true
echo "Built out/linux/Capri and added Capri to your app menu."
