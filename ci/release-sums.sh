#!/usr/bin/env bash
set -euo pipefail
tag=${1:?usage: release-sums.sh <tag> [--dry-run]}
dry=${2:-}
repo=${GITHUB_REPOSITORY:-lachydotmcg/capri-desktop}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir "$work/assets" "$work/sums"
fetch() {
    local id
    id=$(awk -v n="$1" 'substr($0, index($0, " ") + 1) == n { print $1; exit }' "$work/listing")
    gh api -H 'Accept: application/octet-stream' "repos/$repo/releases/assets/$id" > "$2"
}

if ! [[ "$tag" =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "::error::$tag isn't a tag name."
    exit 1
fi
gh api "repos/$repo/releases?per_page=100" --paginate --jq ".[] | select(.tag_name == \"$tag\") | .id" | head -n1 > "$work/release"
release=$(cat "$work/release")
if [ -z "$release" ]; then
    echo "::error::No release has the tag $tag."
    exit 1
fi
gh api "repos/$repo/releases/$release/assets?per_page=100" --paginate --jq '.[] | "\(.id) \(.name)"' > "$work/listing"
cut -d' ' -f2- "$work/listing" > "$work/names"
grep -vx 'SHA256SUMS.txt' "$work/names" | LC_ALL=C sort > "$work/files" || true
if [ ! -s "$work/files" ]; then
    echo "::error::$tag has no assets to check."
    exit 1
fi
for want in Capri-Setup.exe Capri-mac-apple-silicon.zip Capri-mac-intel.zip Capri-linux-x64.AppImage Capri-linux-x64.tar.gz; do
    grep -qx "$want" "$work/files" || echo "::warning::$tag has no $want."
done

while IFS= read -r name; do
    fetch "$name" "$work/assets/$name"
done < "$work/files"
while IFS= read -r name; do
    hash=$(sha256sum -- "$work/assets/$name" | cut -c1-64)
    echo "$hash $name"
done < "$work/files" > "$work/made"

if grep -qx 'SHA256SUMS.txt' "$work/names"; then
    fetch SHA256SUMS.txt "$work/sums/SHA256SUMS.txt"
    sed -E 's/\r$//; s/^([0-9a-fA-F]{64})[ \t]+\*?/\1 /' "$work/sums/SHA256SUMS.txt" | grep -E '^[0-9a-fA-F]{64} ' | awk '{ print tolower($1) " " substr($0, 66) }' | LC_ALL=C sort -k2 > "$work/have"
    LC_ALL=C sort -k2 "$work/made" > "$work/want"
    if diff -u "$work/want" "$work/have" > "$work/diff"; then
        echo "SHA256SUMS.txt on $tag matches all $(wc -l < "$work/want") assets."
        cat "$work/made"
        exit 0
    fi
    echo "::error::SHA256SUMS.txt on $tag doesn't match its assets. Left as it is; check the files, delete SHA256SUMS.txt and run this again to rebuild it."
    cat "$work/diff"
    exit 1
fi

cp "$work/made" "$work/SHA256SUMS.txt"
cat "$work/SHA256SUMS.txt"
if [ "$dry" = "--dry-run" ]; then
    echo "Dry run: SHA256SUMS.txt not uploaded."
    exit 0
fi
gh api -X POST "https://uploads.github.com/repos/$repo/releases/$release/assets?name=SHA256SUMS.txt" -H 'Content-Type: text/plain' --input "$work/SHA256SUMS.txt" --jq .browser_download_url
echo "Uploaded SHA256SUMS.txt for $(wc -l < "$work/made") assets to $tag."
