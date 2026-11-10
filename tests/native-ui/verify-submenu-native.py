#!/usr/bin/env python3
"""Check already captured native menu metadata offline; pixels and input require separate review."""
import argparse
import json
from pathlib import Path
import re


def require(condition, message):
    if not condition:
        raise ValueError(message)


def trace_identity(rows):
    require(rows, 'Trace is empty.')
    identity = None
    for index, row in enumerate(rows, 1):
        pid = row.get('pid')
        name = row.get('nativeMenuClass')
        require(type(pid) is int and pid > 0, f'Trace row {index}: missing or invalid process identity.')
        require(isinstance(name, str) and re.fullmatch(r'Capri\.Passive\.' + str(pid) + r'\.[0-9a-f]{32}Menu', name),
                f'Trace row {index}: missing or invalid process-specific menu class.')
        current = (pid, name)
        require(identity is None or current == identity, f'Trace row {index}: process/menu identity changed.')
        identity = current
    return identity


def verify(trace, headers, captures):
    rows = [json.loads(line) for line in Path(trace).read_text().splitlines() if line.strip()]
    require(all(isinstance(row, dict) for row in rows), 'Trace rows must be objects.')
    pid, menu_class = trace_identity(rows)
    required = set(headers)
    require(required, 'At least one submenu header is required.')
    opens = [row for row in rows if row.get('stage') == 'submenu-opened'
             and row.get('data', {}).get('header') in required]
    seen, checks = set(), []
    for item in opens:
        header = item['data']['header']
        xid = item['data'].get('host', {}).get('xid')
        require(type(xid) is int and xid > 0, f'{header}: missing or invalid native host XID.')
        seen.add(header)
        matches = [row['data'] for row in rows if row.get('stage') == 'native-attributes'
                   and row.get('data', {}).get('parent', {}).get('xid') == xid
                   and row['data']['parent'].get('found') is True
                   and row['data']['parent'].get('mapState') == 2]
        require(matches, f'{header} XID{xid}: no mapped native attribute sample.')
        require(any(data['parent'].get('overrideRedirect') is False
                    and (data.get('renderChild') or {}).get('mapState') == 2 for data in matches),
                f'{header} XID{xid}: not a managed mapped submenu/render child.')
        checks.append({'header': header, 'xid': xid, 'status': 'PASS_MANAGED_NATIVE_MAP'})
    require(required <= seen, 'Missing actual submenu opens: ' + str(sorted(required - seen)))
    for path in captures:
        data = json.loads(Path(path).read_text())
        state = data.get('state')
        require(isinstance(state, dict) and type(state.get('pid')) is int and state['pid'] == pid,
                f'{Path(path).name}: capture process does not match the trace.')
        clients = data.get('clients')
        require(isinstance(clients, list), f'{Path(path).name}: missing compositor client array.')
        menus = [client for client in clients if isinstance(client, dict)
                 and client.get('mapped') is True and type(client.get('pid')) is int
                 and client['pid'] == pid and client.get('class') == menu_class]
        require(len(menus) >= 2, f'{Path(path).name}: expected root and child with the exact trace process/menu class.')
        require(all(client.get('pinned') is True and client.get('floating') is True for client in menus),
                f'{Path(path).name}: process-specific popup rules are missing.')
        checks.append({'capture': str(path), 'status': 'PASS_PROCESS_MENU_CLASS_AND_RULES', 'mapped_menus': len(menus)})
    return {'status': 'PASS_NATIVE_METADATA', 'pid': pid, 'nativeMenuClass': menu_class, 'checks': checks,
            'scope': 'Native classification/map/rule regression only; settled actual pixels, stacking, input and human appearance remain separate checks.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace', type=Path)
    parser.add_argument('--header', action='append')
    parser.add_argument('--capture', type=Path, action='append', default=[])
    args = parser.parse_args()
    try:
        result = verify(args.trace, args.header or ['Size', 'Move', 'Help'], args.capture)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, f'Submenu evidence check failed: {error}\n')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
