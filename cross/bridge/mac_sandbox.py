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


def runtime():
    import ctypes
    import ssl

    dyld = ctypes.CDLL(None)
    dyld._dyld_image_count.argtypes = []
    dyld._dyld_image_count.restype = ctypes.c_uint32
    dyld._dyld_get_image_name.argtypes = [ctypes.c_uint32]
    dyld._dyld_get_image_name.restype = ctypes.c_char_p
    images = [Path(os.fsdecode(dyld._dyld_get_image_name(index))).resolve()
              for index in range(dyld._dyld_image_count())]
    executable = Path(sys.executable).absolute()
    prefixes = {Path(value).resolve() for value in (sys.base_prefix, sys.base_exec_prefix, sys.prefix, sys.exec_prefix)}
    for directory in (executable.parent, executable.parent.parent):
        if (directory / 'pyvenv.cfg').is_file():
            prefixes.add(directory.resolve())
    broad = {Path(value).resolve() for value in ('/', '/Users', '/tmp', '/private/tmp', '/usr', '/usr/local', '/opt', '/opt/homebrew', '/Library', '/private', '/var')}
    if not images or not images[0].is_file() or prefixes & (broad | {Path.home().resolve()}):
        raise ValueError('Runtime reference is too broad or missing')
    return dict(executable=str(images[0]), launcher=str(executable), base_prefix=sys.base_prefix,
                prefixes={str(path) for path in prefixes}, libraries={str(path) for path in images})


def profile(reads, writes, runtime=None):
    rules = ['(version 1)', '(deny default)',
             '(allow process-fork)',
             '(allow signal (target same-sandbox))',
             '(allow process-info* (target same-sandbox))',
             '(allow process-info-pidinfo process-info-setcontrol (target self))',
             '(allow sysctl-read)', '(allow file-read-metadata)',
             '(allow network*)',
             '(allow system-socket (require-all (socket-domain AF_SYSTEM) (socket-protocol 2)))',
             '(allow mach-lookup (global-name "com.apple.system.opendirectoryd.libinfo") '
             '(global-name "com.apple.SystemConfiguration.configd") '
             '(global-name "com.apple.SystemConfiguration.DNSConfiguration") '
             '(global-name "com.apple.networkd") '
             '(global-name "com.apple.bsd.dirhelper") '
             '(global-name "com.apple.system.opendirectoryd.membership") '
             '(global-name "com.apple.logd") '
             '(global-name "com.apple.system.logger") '
             '(global-name "com.apple.system.notification_center") '
             '(global-name "com.apple.cfprefsd.daemon") '
             '(global-name "com.apple.cfprefsd.agent") '
             '(global-name "com.apple.diagnosticd") '
             '(global-name "com.apple.SecurityServer") '
             '(global-name "com.apple.ocspd") '
             '(global-name "com.apple.trustd.agent"))',
             '(allow ipc-posix-shm-read* (ipc-posix-name "apple.shm.notification_center") '
             '(ipc-posix-name-regex #"^apple[.]cfprefs[.]"))',
             '(allow file-read* file-write-data '
             '(require-all (require-any (literal "/dev/null") (literal "/dev/zero")) (vnode-type CHARACTER-DEVICE)))',
             '(allow file-read* file-write-data file-ioctl '
             '(require-all (literal "/dev/dtracehelper") (vnode-type CHARACTER-DEVICE)))',
             '(allow file-read-data (literal "/Library/Preferences/.GlobalPreferences.plist") '
             '(literal "/Library/Managed Preferences/.GlobalPreferences.plist"))']
    for path in sorted(reads | writes):
        rules.append('(allow file-read* (subpath ' + quoted(path) + '))')
    for path in sorted(writes):
        rules.append('(allow file-write* (subpath ' + quoted(path) + '))')
    if runtime is not None:
        rules.append('(allow process-exec (literal ' + quoted(runtime['executable']) + '))')
        for path in sorted(runtime['prefixes']):
            rules.append('(allow file-read* file-map-executable (subpath ' + quoted(path) + '))')
        for path in sorted(runtime['libraries']):
            rules.append('(allow file-read* file-map-executable (literal ' + quoted(path) + '))')
    for path in ('/System/Library', '/usr/lib', '/System/Cryptexes/App', '/System/Cryptexes/OS',
                 '/System/Volumes/Preboot/Cryptexes/App/System', '/System/Volumes/Preboot/Cryptexes/OS'):
        rules.append('(allow file-read* file-map-executable (subpath ' + quoted(path) + '))')
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
    resolved = runtime()
    reads = {worker.parent}
    if worker.parent.resolve() in {Path(path).resolve() for path in ('/', '/Users', '/tmp', '/private/tmp')} | {Path.home().resolve()}:
        raise ValueError('Runtime reference is too broad')
    reads |= {Path(path) for path in ('/usr/share/locale', '/usr/share/icu', '/usr/share/zoneinfo', '/usr/share/zoneinfo.default',
        '/private/var/db/timezone', '/dev/null', '/dev/random', '/dev/urandom', '/private/etc/ssl', '/private/etc/localtime',
        '/private/etc/passwd', '/private/etc/resolv.conf', '/private/etc/hosts', '/private/etc/services', '/private/etc/protocols', '/var/empty')}
    reads.add(resource)
    reads = {str(path.resolve()) for path in reads}
    writes = {str(resource.resolve())} if args.kind == 'account' else set()
    environment = {'PATH': '/usr/bin:/bin', 'HOME': '/var/empty', 'TMPDIR': '/var/empty',
        'LANG': 'en_US.UTF-8', '__PYVENV_LAUNCHER__': resolved['launcher']}
    os.chdir('/var/empty')
    os.execve('/usr/bin/sandbox-exec', ['sandbox-exec', '-p', profile(reads, writes, resolved),
              resolved['executable'], '-B', '-E', '-s', '-u', str(worker)], environment)
