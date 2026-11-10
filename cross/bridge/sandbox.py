"""Launch retained workers with explicit filesystem and capability mounts."""
import argparse
import json
import os
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--kind', choices=('plan', 'account', 'voice', 'ears', 'decoder'), required=True)
    parser.add_argument('--python', required=True)
    parser.add_argument('--worker', required=True)
    parser.add_argument('--resource', required=True)
    parser.add_argument('--cache')
    args = parser.parse_args()
    if sys.platform == 'darwin':
        from mac_sandbox import launch
        launch(args)
        return
    python = Path(args.python).absolute()
    worker = Path(args.worker).absolute()
    if not python.is_file() or not worker.is_file():
        raise ValueError('Worker runtime missing')
    reads = {str(worker.parent.parent if args.kind not in ('plan', 'account') else worker.parent), str(python.parent.parent), str(python.resolve().parent.parent)}
    if python.is_symlink() and python.readlink().is_absolute():
        reads.add(str(python.readlink().parent.parent))
    writes = set()
    resource = Path(args.resource).absolute()
    if args.kind in ('plan', 'account'):
        if args.kind == 'account':
            if resource.is_symlink() or not (resource / 'native-owner-v1').is_file():
                raise ValueError('Only a separately initialized native account can be written')
            writes.add(str(resource))
        else: reads.add(str(resource))
    else:
        visited = set()
        def inspect(path):
            path = path.absolute()
            if str(path) in visited or len(visited) >= 8 or path.stat().st_size > 1024 * 1024:
                raise ValueError('Invalid recursive runtime manifest')
            visited.add(str(path)); reads.add(str(path))
            def walk(value, key=''):
                if isinstance(value, dict):
                    for name, child in value.items(): walk(child, name)
                elif isinstance(value, list):
                    for child in value: walk(child, key)
                elif isinstance(value, str) and value.startswith('/'):
                    target = Path(value)
                    if str(target) in ('/', '/home', str(Path.home()), '/tmp', '/run'):
                        raise ValueError('Runtime reference is too broad')
                    if key == 'cache_dir': writes.add(str(target))
                    elif key == 'base_profile': inspect(target)
                    elif target.exists(): reads.add(str(target))
            walk(json.loads(path.read_text()))
        inspect(resource)
    if args.cache:
        writes.add(str(Path(args.cache).absolute()))
    for path in writes:
        Path(path).mkdir(parents=True, exist_ok=True, mode=0o700)
    command = ['/usr/bin/bwrap', '--die-with-parent', '--new-session', '--unshare-pid', '--unshare-ipc', '--unshare-uts', '--clearenv', '--proc', '/proc', '--dev', '/dev', '--tmpfs', '/tmp', '--setenv', 'PATH', '/usr/bin:/bin', '--setenv', 'HOME', '/tmp', '--setenv', 'PYTHONDONTWRITEBYTECODE', '1', '--setenv', 'PYTHONNOUSERSITE', '1', '--setenv', 'LANG', 'C.UTF-8']
    if args.kind not in ('plan', 'account'): command += ['--unshare-net', '--setenv', 'HF_HUB_OFFLINE', '1', '--setenv', 'TRANSFORMERS_OFFLINE', '1']
    for path in ('/usr', '/bin', '/lib', '/lib64', '/etc/ld.so.cache', '/etc/ssl', '/etc/resolv.conf', '/etc/hosts', '/etc/nsswitch.conf'):
        if Path(path).exists(): command += ['--ro-bind', path, path]
    for path in sorted(reads, key=lambda value: (value.count('/'), value)):
        if path.startswith(('/usr/', '/bin/', '/lib/', '/lib64/')): continue
        if not Path(path).exists(): raise ValueError('Missing retained resource')
        command += ['--ro-bind', path, path]
    for path in sorted(writes): command += ['--bind', path, path]
    if args.kind == 'ears':
        runtime = os.environ.get('XDG_RUNTIME_DIR', '')
        remote = Path(runtime) / 'pipewire-0'
        if not runtime or not remote.is_socket(): raise ValueError('PipeWire is unavailable')
        command += ['--ro-bind', str(remote), str(remote), '--setenv', 'XDG_RUNTIME_DIR', runtime]
    command += ['--chdir', '/tmp', '--', str(python), '-u', str(worker)]
    if args.kind == 'voice': command += ['--profile', str(resource)]
    if args.kind == 'decoder': command += ['--manifest', str(resource)]
    os.execv(command[0], command)


if __name__ == '__main__':
    main()
