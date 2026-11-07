#!/bin/sh
# Builds Capri for Linux into out/linux and installs a launcher for it in your app menu.
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet; else DOTNET="$HOME/.dotnet/dotnet"; export DOTNET_ROOT="$HOME/.dotnet"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$DOTNET" publish cross/Capri.Cross.csproj -c Release -r linux-x64 --self-contained -o out/linux -p:DebugType=none
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
icons="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
mkdir -p "$apps" "$icons"
cp cross/icon.png "$icons/capri.png"
sed "s|@EXEC@|$PWD/out/linux/Capri|" linux/capri.desktop > "$apps/capri.desktop"
update-desktop-database "$apps" 2>/dev/null || true
echo "Built out/linux/Capri and added Capri to your app menu."
