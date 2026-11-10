#!/usr/bin/env python3
"""Extract bounded production regions; fail on changed/ambiguous boundary markers."""
import hashlib,json,sys
from pathlib import Path
source=Path(sys.argv[1]); target=Path(sys.argv[2]); target.mkdir(parents=True,exist_ok=True)
def span(text,start,end):
    if text.count(start)!=1 or text.count(end)!=1:raise SystemExit('Production extraction boundary changed: '+start)
    a=text.index(start);b=text.index(end,a)
    return text[a:b]
hands=(source/'app/Hands.cs').read_text();pet=(source/'cross/Pet.cs').read_text();limbs=(source/'cross/Limbs.cs').read_text()
security=span(hands,'using System;','    internal sealed class OpenWindow')
(target/'SecuritySource.cs').write_text(security+'\n}\n')
settings=span(pet,'        private Settings Load()','        // After her hello, while nothing else is showing')+span(pet,'        internal void Save()','        internal void Quit()')
(target/'SettingsSource.cs').write_text('using System;using System.IO;using System.Threading;using System.Runtime.Serialization;using System.Runtime.Serialization.Json;namespace CapriDesktop { partial class Pet {\n'+settings+'\n}}')
power=span(limbs,'        internal static string[][] PowerCommands(','        internal static bool Power(')
(target/'PowerSource.cs').write_text('using System;using System.Text.RegularExpressions;namespace CapriDesktop { static class Limbs {\n'+power+'\n}}')
(target/'EXTRACTION_MANIFEST.json').write_text(json.dumps({f:hashlib.sha256((source/f).read_bytes()).hexdigest() for f in ['app/Hands.cs','cross/Pet.cs','cross/Limbs.cs']},indent=2)+'\n')
