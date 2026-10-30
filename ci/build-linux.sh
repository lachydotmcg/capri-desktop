#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
output=${1:?Usage: ci/build-linux.sh /absolute/output-directory}
if [[ $output != /* ]]; then
    printf '%s\n' 'The output directory must be absolute.' >&2
    exit 2
fi
if [[ -e $output ]]; then
    printf '%s\n' 'The output directory already exists; choose a new directory.' >&2
    exit 2
fi

make -C "$root/cross/native"
dotnet publish "$root/cross/Capri.Cross.csproj" -c Release -r linux-x64 \
    --self-contained true -o "$output" -p:RestoreLockedMode=true \
    -p:NuGetLockFilePath=packages.linux-x64.lock.json \
    -p:DebugType=none
python3 "$root/ci/copy-linux-notices.py" "$root/cross/obj/project.assets.json" "$output"
install -Dm755 "$root/cross/native/capri-input" "$output/native/capri-input"
install -Dm644 "$root/LICENSE.md" "$output/LICENSE.md"
install -Dm644 "$root/assets/fonts/OFL-ShareTechMono.txt" "$output/licenses/OFL-ShareTechMono.txt"
for notice in "$root"/cross/licenses/*; do
    install -Dm644 "$notice" "$output/licenses/$(basename -- "$notice")"
done
if [[ -f $root/cross/native/LICENSE ]]; then
    install -Dm644 "$root/cross/native/LICENSE" "$output/licenses/native-input-LICENSE"
fi
