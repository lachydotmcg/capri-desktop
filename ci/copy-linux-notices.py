#!/usr/bin/env python3
"""Retain published dependency metadata and the notices shipped in each package."""

import hashlib
import json
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

assets = json.loads(Path(sys.argv[1]).read_text())
output = Path(sys.argv[2])
dependencies = json.loads((output / "Capri.deps.json").read_text())["libraries"]
roots = [Path(folder) for folder in assets["packageFolders"]]
manifest = []
for identity, metadata in sorted(dependencies.items()):
    if metadata["type"] not in ("package", "runtimepack"):
        continue
    name, version = identity.removeprefix("runtimepack.").split("/")
    relative = Path(name.lower()) / version
    if any(part in ("", ".", "..") for part in relative.parts):
        raise ValueError("Invalid package identity")
    folder = next((root / relative for root in roots if (root / relative).is_dir()), None)
    if folder is None:
        raise FileNotFoundError("Published package not found: " + identity)
    destination = output / "licenses" / name / version
    destination.mkdir(parents=True)
    record = {"name": name, "version": version, "type": metadata["type"],
              "content_hash": metadata.get("sha512", ""), "notices": []}
    archive = folder / (name.lower() + "." + version + ".nupkg")
    with archive.open("rb") as stream:
        record["archive_sha256"] = hashlib.file_digest(stream, "sha256").hexdigest()
    nuspec = next(folder.glob("*.nuspec"))
    for element in ET.parse(nuspec).iter():
        key = element.tag.rsplit("}", 1)[-1]
        if key in ("license", "licenseUrl", "copyright") and element.text:
            record[key] = element.text.strip()
    for source in sorted(folder.iterdir()):
        if source.is_file() and (source.suffix == ".nuspec" or
                source.name.lower().startswith(("license", "third-party-notice", "notice", "copyright"))):
            shutil.copyfile(source, destination / source.name)
            record["notices"].append(str((destination / source.name).relative_to(output)))
    manifest.append(record)
(output / "DEPENDENCIES.json").write_text(json.dumps(manifest, indent=2) + "\n")
