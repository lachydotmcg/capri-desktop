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
install -Dm644 "$root/THIRD-PARTY-NOTICES.md" "$output/THIRD-PARTY-NOTICES.md"
install -Dm644 "$root/assets/fonts/OFL-ShareTechMono.txt" "$output/licenses/OFL-ShareTechMono.txt"
install -Dm644 "$root/assets/fonts/Barlow-OFL.txt" "$output/licenses/Barlow-OFL.txt"
install -d "$output/tools"
for support in "$root"/tools/*.py "$root"/tools/*.lock "$root"/tools/*.json; do
    install -m644 "$support" "$output/tools/$(basename -- "$support")"
done
install -Dm644 "$root/LINUX.md" "$output/LINUX.md"
if [[ -f $root/cross/native/LICENSE ]]; then
    install -Dm644 "$root/cross/native/LICENSE" "$output/licenses/native-input-LICENSE"
fi
