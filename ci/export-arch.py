#!/usr/bin/env python3
"""Export one committed source tree and a checksum-bound Arch package recipe."""

import argparse
import gzip
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


def git(root, *arguments):
    return subprocess.check_output(["git", "-C", str(root), *arguments])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--revision", default="HEAD")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    commit = git(root, "rev-parse", "--verify", "--end-of-options", args.revision + "^{commit}").decode().strip()
    tree = git(root, "rev-parse", commit + "^{tree}").decode().strip()
    project = ET.fromstring(git(root, "show", commit + ":cross/Capri.Cross.csproj"))
    version = project.findtext("PropertyGroup/Version")
    if not version or not all(c.isdigit() or c == "." for c in version):
        raise ValueError("The committed project must have a numeric version")
    timestamp = git(root, "show", "-s", "--format=%ct", commit).decode().strip()
    package_version = version + ".r" + timestamp + ".g" + commit[:12]
    destination = args.destination.resolve()
    destination.mkdir(parents=True, exist_ok=False)
    filename = "capri-" + commit + ".tar.gz"
    archive = destination / filename
    with archive.open("xb") as output:
        with gzip.GzipFile(filename="", mode="wb", fileobj=output, mtime=0) as compressed:
            process = subprocess.Popen(["git", "-C", str(root), "archive", "--format=tar",
                                        "--prefix=capri/", commit], stdout=subprocess.PIPE)
            try:
                while data := process.stdout.read(1024 * 1024):
                    compressed.write(data)
            finally:
                process.stdout.close()
            if process.wait() != 0:
                raise RuntimeError("git archive failed")
    with archive.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    template = git(root, "show", commit + ":packaging/arch/PKGBUILD.in").decode()
    for key, value in {"@VERSION@": package_version, "@SOURCE@": filename,
                       "@SOURCE_SHA256@": digest, "@COMMIT@": commit}.items():
        template = template.replace(key, value)
    (destination / "PKGBUILD").write_text(template)
    receipt = {"commit": commit, "tree": tree, "commit_timestamp": int(timestamp), "source": filename,
               "source_sha256": digest, "package_version": package_version}
    (destination / "SOURCE.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
