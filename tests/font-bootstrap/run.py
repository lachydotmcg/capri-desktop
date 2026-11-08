#!/usr/bin/env python3
"""No-GUI production-linked HarfBuzz loader regression, with isolated no-core children."""
import argparse
import json
import pathlib
import subprocess
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument("assembly", type=pathlib.Path)
parser.add_argument("--output", type=pathlib.Path)
args = parser.parse_args()
if not sys.platform.startswith("linux"):
    print("SKIP Linux font-bootstrap checks")
    raise SystemExit(0)

import resource

assembly = args.assembly.resolve(strict=True)
results = []

def disable_cores():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))

for mode in ("baseline-clean", "baseline-global", "candidate-before-global", "candidate-global", "paths"):
    started = time.monotonic()
    try:
        run = subprocess.run(
            ["dotnet", str(assembly), mode], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, timeout=15, preexec_fn=disable_cores)
        if run.returncode == 77:
            status = "UNVERIFIED_SYSTEM_LIBRARY_UNAVAILABLE"
        elif mode == "baseline-global" and run.returncode < 0:
            status = "REPRODUCED_NATIVE_BASELINE_FAILURE"
        elif mode == "baseline-global" and run.returncode == 0:
            status = "BASELINE_ABI_COMPATIBLE_ON_THIS_SYSTEM"
        else:
            status = "PASS" if run.returncode == 0 else "FAIL"
        row = dict(mode=mode, status=status, exit=run.returncode,
                   seconds=round(time.monotonic() - started, 3), output=run.stdout, core_dump_limit=0)
    except subprocess.TimeoutExpired as error:
        row = dict(mode=mode, status="FAIL_TIMEOUT", seconds=15,
                   output=str(error), core_dump_limit=0)
    results.append(row)
    print(json.dumps(row), flush=True)

if args.output:
    args.output.write_text(json.dumps(results, indent=2) + "\n")
if any(row["status"].startswith("FAIL") for row in results):
    raise SystemExit(1)
