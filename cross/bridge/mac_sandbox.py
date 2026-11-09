"""Seatbelt boundary for the retained ChatGPT account and inference workers."""
import json
import os
from pathlib import Path
import sys


def quoted(value):
    value = str(value)
    if any(ord(c) < 32 or ord(c) == 127 for c in value):
        raise ValueError('Invalid sandbox path')
    return json.dumps(value, ensure_ascii=False)


def profile(reads, writes):
    rules = ['(version 1)', '(deny default)',
             '(allow process-exec process-fork)',
             '(allow signal (target same-sandbox))',
             '(allow process-info* (target same-sandbox))',
             '(allow sysctl-read)', '(allow file-read-metadata)',
             '(allow network*)',
             '(allow system-socket (require-all (socket-domain AF_SYSTEM) (socket-protocol 2)))',
             '(allow mach-lookup (global-name "com.apple.system.opendirectoryd.libinfo") '
             '(global-name "com.apple.SystemConfiguration.configd") '
             '(global-name "com.apple.SystemConfiguration.DNSConfiguration") '
             '(global-name "com.apple.networkd") '
             '(global-name "com.apple.bsd.dirhelper") '
             '(global-name "com.apple.system.opendirectoryd.membership") '
             '(global-name "com.apple.SecurityServer") '
             '(global-name "com.apple.ocspd") '
             '(global-name "com.apple.trustd.agent"))',
             '(allow file-write-data (literal "/dev/null"))']
    for path in sorted(reads | writes):
        rules.append('(allow file-read* (subpath ' + quoted(path) + '))')
    for path in sorted(writes):
        rules.append('(allow file-write* (subpath ' + quoted(path) + '))')
    return '\n'.join(rules)


def launch(args):
    if args.kind not in ('plan', 'account') or args.cache is not None:
        raise ValueError('This retained worker has no qualified Mac sandbox')
    python, worker, resource = (Path(value).absolute() for value in (args.python, args.worker, args.resource))
    if not python.is_file() or not worker.is_file() or resource.is_symlink() or not resource.is_dir():
        raise ValueError('Retained worker setup is missing')
    if resource.stat().st_uid != os.getuid() or resource.stat().st_mode & 0o077:
        raise ValueError('The account directory must be private')
    if args.kind == 'account' and not (resource / 'native-owner-v1').is_file():
        raise ValueError('Only an initialized native account can be written')
    reads = {worker.parent, python.parent.parent, python.resolve().parent.parent, Path(sys.base_prefix)}
    if any(path in (Path('/'), Path.home(), Path('/Users'), Path('/tmp'), Path('/private/tmp')) for path in reads):
        raise ValueError('Runtime reference is too broad')
    reads |= {Path(path) for path in ('/System', '/usr/lib', '/usr/share', '/usr/local', '/opt/homebrew', '/dev/null', '/dev/random', '/dev/urandom',
        '/private/etc/ssl', '/private/etc/resolv.conf', '/private/etc/hosts', '/private/etc/services', '/private/etc/protocols', '/var/empty')}
    reads.add(resource)
    reads = {str(path.resolve()) for path in reads}
    writes = {str(resource.resolve())} if args.kind == 'account' else set()
    environment = {'PATH': '/usr/bin:/bin', 'HOME': '/var/empty', 'TMPDIR': '/var/empty',
        'LANG': 'en_US.UTF-8', 'PYTHONDONTWRITEBYTECODE': '1', 'PYTHONNOUSERSITE': '1'}
    os.chdir('/var/empty')
    os.execve('/usr/bin/sandbox-exec', ['sandbox-exec', '-p', profile(reads, writes), str(python), '-u', str(worker)], environment)
