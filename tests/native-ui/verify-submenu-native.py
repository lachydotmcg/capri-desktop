#!/usr/bin/env python3
"""Offline assertions over already captured public native QA evidence. No GUI."""
import argparse,json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('trace',type=Path);p.add_argument('--header',action='append');p.add_argument('--capture',type=Path,action='append',default=[]);a=p.parse_args()
rows=[json.loads(line) for line in a.trace.read_text().splitlines() if line.strip()];required=set(a.header or ['Size','Move','Help']);opens=[r for r in rows if r['stage']=='submenu-opened' and r['data']['header'] in required];seen=set();checks=[]
for item in opens:
 header=item['data']['header'];xid=item['data']['host']['xid'];seen.add(header)
 matches=[r['data'] for r in rows if r['stage']=='native-attributes' and r['data'].get('parent',{}).get('xid')==xid and r['data']['parent'].get('found') and r['data']['parent'].get('mapState')==2]
 assert matches,f'{header} XID{xid} has no mapped native attribute sample'
 assert any(not d['parent']['overrideRedirect'] and (d.get('renderChild') or {}).get('mapState')==2 for d in matches),f'{header} XID{xid} is not a managed mapped submenu/render child'
 checks.append({'header':header,'xid':xid,'status':'PASS_MANAGED_NATIVE_MAP'})
assert required<=seen,'Missing actual submenu opens: '+str(sorted(required-seen))
for path in a.capture:
 data=json.loads(path.read_text());menus=[c for c in data['clients'] if c.get('mapped') and c.get('class','').endswith('Menu')]
 assert len(menus)>=2,f'{path.name}: expected mapped root and child process-classified menus'
 assert all(c.get('pinned') and c.get('floating') for c in menus),f'{path.name}: process-scoped popup rules missing'
 checks.append({'capture':str(path),'status':'PASS_PROCESS_MENU_CLASS_AND_RULES','mapped_menus':len(menus)})
print(json.dumps({'status':'PASS_NATIVE_METADATA','checks':checks,'scope':'Native classification/map/rule regression only; settled actual pixel stacking, keyboard/outside/cursor and human appearance remain independent checks.'},indent=2))
