import re
import sys
from pathlib import Path


def method(source, name):
    start = re.search(r"(?m)^([ \t]+)((?:private|internal) [^\n]*\b" + name + r"\([^()\n]*\))[ \t]*\n\1\{", source)
    if start is None:
        raise ValueError("missing hook method " + name)
    end = re.search(r"(?m)^" + re.escape(start[1]) + r"\}", source[start.end():])
    if end is None:
        raise ValueError("missing end of " + name)
    return start[2], source[start.end():start.end() + end.start()]


def ordered(body, dispatch):
    stamp = re.search(r"\bModMessage\s*\(|\bAt\s*=\s*DateTime\.Now\b", body)
    snapshot = re.search(dispatch, body)
    return stamp is not None and snapshot is not None and stamp.start() < snapshot.start()


def preserves_timestamp(wrapper, shared):
    eager = r"\b(?:LiveHosts|ModsFire)\s*\("
    if re.search(eager, wrapper):
        return ordered(wrapper, eager)
    call = re.search(r"\bModsShared\.(\w+)\s*\([^;]*\bLiveHosts\s*[,)]", wrapper)
    if call is None:
        return False
    signature, body = method(shared, call[1])
    supplier = re.search(r"Func<IModHookHost\[\]>\s+(\w+)", signature)
    return supplier is not None and ordered(body, r"\b" + supplier[1] + r"\s*\(")


root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
shared_path = root / "app/ModsShared.cs"
shared = shared_path.read_text(encoding="utf-8") if shared_path.exists() else ""
failures = 0
for file in ("app/ModHooks.cs", "cross/Pet.Mods.cs"):
    source = (root / file).read_text(encoding="utf-8")
    for name in ("ModsSending", "ModsReplying", "ModsReplied", "ModsSpeaking"):
        ok = preserves_timestamp(method(source, name)[1], shared)
        print(f"{'PASS' if ok else 'FAIL'} {file}:{name}: timestamp before delivery snapshot")
        failures += not ok
sys.exit(bool(failures))
