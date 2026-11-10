#!/usr/bin/env python3
"""Explicit pinned pack staging/profile creation; never changes the live voice."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "cross" / "bridge") if (Path(__file__).resolve().parents[1] / "cross" / "bridge").is_dir() else str(Path(__file__).resolve().parents[1] / "bridge"))
from speech.voice_pack_setup import download_archive, stage_archive, create_profile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="operation", required=True)
    download = sub.add_parser("download")
    download.add_argument("--directory", type=Path, required=True)
    stage = sub.add_parser("stage")
    stage.add_argument("--archive", type=Path, required=True)
    stage.add_argument("--output", type=Path, required=True)
    profile = sub.add_parser("profile", help="Run with the existing isolated Kokoro Python")
    profile.add_argument("--data", type=Path, required=True)
    profile.add_argument("--runtime-profile", type=Path, required=True)
    profile.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.operation == "download":
        result = {"archive": str(download_archive(args.directory)), "status": "VERIFIED_NOT_STAGED"}
    elif args.operation == "stage":
        result = stage_archive(args.archive, args.output)
    else:
        value = create_profile(args.data, args.runtime_profile, args.output)
        result = {"profile": str(args.output), "variant": value["variant"], "status": "VERIFIED_NOT_SELECTED"}
    print(json.dumps(result))


if __name__ == "__main__":
    main()
