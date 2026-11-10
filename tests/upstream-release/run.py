#!/usr/bin/env python3
"""Run bounded release regressions against current production source in disposable output."""
import argparse, os, shutil, subprocess, sys, tempfile
from pathlib import Path
here=Path(__file__).resolve().parent
parser=argparse.ArgumentParser()
parser.add_argument('--source',type=Path,default=here.parents[1],help='native source root (contains app/Hands.cs and cross/Pet.cs)')
args=parser.parse_args();source=args.source.resolve()
for name in ['app/Hands.cs','cross/Pet.cs','cross/Limbs.cs']:
    if not (source/name).is_file():parser.error('Missing production input: '+str(source/name))
with tempfile.TemporaryDirectory(prefix='capri-upstream-regression-') as taskdir:
    task=Path(taskdir)
    for name in ['Program.cs','Stubs.cs','Checks.csproj']:shutil.copy2(here/name,task/name)
    subprocess.run([sys.executable,str(here/'extract.py'),str(source),str(task)],check=True)
    print('Production SHA256 inputs:',flush=True)
    print((task/'EXTRACTION_MANIFEST.json').read_text(),flush=True)
    subprocess.run(['dotnet','run','--project',str(task/'Checks.csproj')],cwd=task,check=True)
