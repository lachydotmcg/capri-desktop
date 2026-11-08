"""Bind an exec'd native child to its exact launcher without preexec hooks."""
import ctypes
import os
import signal
import sys
owner=int(sys.argv[1])
if os.getppid()!=owner or sys.argv[2]!='--':raise SystemExit(125)
if ctypes.CDLL(None,use_errno=True).prctl(1,signal.SIGTERM,0,0,0)!=0 or os.getppid()!=owner:raise SystemExit(125)
os.execv(sys.argv[3],sys.argv[3:])
