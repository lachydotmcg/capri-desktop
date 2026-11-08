#!/usr/bin/env python3
"""Create a private, versioned native export; never modify the live companion DB."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sqlite3
from datetime import datetime, timezone

VERSION = 1


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write(path, value):
    data = value if isinstance(value, str) else json.dumps(value, ensure_ascii=True, indent=2) + '\n'
    with path.open('x', encoding='utf-8') as stream:
        stream.write(data)
    path.chmod(0o600)


def migrate(database, account_directory, roles_path, references, destination, *, public_fixture=False):
    os.umask(0o077)
    if destination.exists():
        raise ValueError('Destination already exists; migration never overwrites a live profile.')
    active = json.loads((account_directory / 'active.json').read_text())['account']
    if not isinstance(active, str) or len(active) != 64 or any(c not in '0123456789abcdef' for c in active):
        raise ValueError('Invalid account binding')
    destination.mkdir(mode=0o700, parents=True)
    native = destination / 'Capri'
    native.mkdir(mode=0o700)
    archive = native / 'legacy-export-v1'
    archive.mkdir(mode=0o700)
    source = sqlite3.connect(database.as_uri() + '?mode=ro', uri=True)
    backup = sqlite3.connect(archive / 'companion.sqlite')
    source.backup(backup)
    source.close()
    def state(partition, key, default):
        row = backup.execute('SELECT value FROM app_state WHERE partition=? AND key=?', (partition, key)).fetchone()
        return json.loads(row[0]) if row else default
    profile = state('local-owner', 'profile', {})
    presentation = state('local-owner', 'presentation-v8', {})
    conversation = state(active, 'conversation-presentation-v8', {})
    audio = state(active, 'presentation-audio-v4', {})
    desktop = state(active, 'desktop-observation-v5', {})
    personality = state(active, 'personalization-v043', {'name': '', 'persona': '', 'lines': []})
    roles = json.loads(roles_path.read_text())['profiles']
    def role(name):
        key = hashlib.sha256(json.dumps([['chatgpt_plan', active], name], separators=(',', ':')).encode()).hexdigest()
        if key not in roles:
            raise ValueError('Qualified model role missing: ' + name)
        return roles[key]
    chat, gui = role('chat'), role('gui')
    required = {'plan_python', 'voice_python', 'voice_profile', 'asr_python', 'asr_manifest', 'microphone_node'}
    if set(references) != required:
        raise ValueError('Provide the explicit, versioned runtime reference fields.')
    for key in required - {'microphone_node'}:
        if not Path(references[key]).is_absolute() or not Path(references[key]).is_file():
            raise ValueError('Retained runtime reference is unavailable: ' + key)
    level = presentation.get('voice_volume', 53)
    if type(level) is not int or not 0 <= level <= 100:
        raise ValueError('Invalid retained voice volume')
    settings = {
        'LinuxMigrationVersion': VERSION, 'NativePlan': True,
        'PlanPython': references['plan_python'], 'PlanAccounts': str(account_directory),
        'PlanModel': chat['model'], 'PlanVisionModel': gui['model'],
        'PlanEffort': chat['effort'], 'PlanVisionEffort': gui['effort'],
        'PlanTokens': chat['output_tokens'], 'PlanVisionTokens': gui['output_tokens'],
        'OwnedVoicePython': references['voice_python'], 'OwnedVoiceProfile': references['voice_profile'],
        'OwnedAsrPython': references['asr_python'], 'OwnedAsrManifest': references['asr_manifest'],
        'MicrophoneNode': references['microphone_node'], 'VoiceProvider': 'retained-local',
        'Silent': bool(profile.get('mute') or profile.get('quiet') or profile.get('dnd') or audio.get('enabled') is False),
        'Sounds': profile.get('sound_volume', 15), 'Volume': level, 'CubicVoiceVolume': True,
        'NoIntro': not presentation.get('intro_enabled', False), 'NotOnTop': not presentation.get('on_top', True),
        'Watch': False, 'NoCheckIns': bool(public_fixture or profile.get('quiet') or profile.get('dnd') or not profile.get('initiative', False)),
        'NoCaughtUp': True, 'NoDiscord': True, 'NoMemories': False,
        'Model': chat['model'], 'Nickname': '' if public_fixture else personality.get('name', ''),
        'Language': conversation.get('reply_language', 'auto'), 'Recall': conversation.get('memory_turns', 24),
        'Permissions': '11111' + ('1' if desktop.get('enabled') and desktop.get('allow_input') else '0') + '10000',
        'Size': 240, 'StayOnModel': True, 'LastVersion': '0.8.4', 'UpdateSeen': '0.8.4'
    }
    turns = backup.execute('SELECT id,role,text,status,created FROM turns WHERE partition=? ORDER BY id', (active,)).fetchall()
    memories = backup.execute('SELECT id,text,source_turn,created FROM memories WHERE partition=? ORDER BY id', (active,)).fetchall()
    draft = state(active, 'draft', '')
    imported = [row for row in turns if row[1] in ('user', 'assistant') and row[3] == 'completed']
    write(native / 'cross.json', settings)
    write(native / 'draft.txt', '' if public_fixture else draft)
    write(native / 'chat.json', {'at': datetime.now(timezone.utc).isoformat(), 'messages': [] if public_fixture else [{'role': row[1], 'content': row[2]} for row in imported[-60:]]})
    if not public_fixture:
        write(native / 'memories.md', '\n'.join('- ' + row[1] for row in memories) + '\n')
        write(native / 'personality.md', personality.get('persona', '') + '\n' + '\n'.join(personality.get('lines', [])))
    write(archive / 'preserved-state.json', {'version': VERSION, 'partition': active, 'profile': profile, 'presentation': presentation, 'conversation': conversation, 'audio': audio, 'desktop': desktop, 'personalization': personality, 'draft': draft, 'turns': turns, 'memories': memories})
    backup.close()
    for path in native.rglob('*'):
        if path.is_file(): path.chmod(0o600)
    receipt = {'version': VERSION, 'profile_kind': 'public-fixture' if public_fixture else 'private-owner-export', 'source_database_sha256': digest(archive / 'companion.sqlite'), 'counts': {'turns': len(turns), 'completed_context': 0 if public_fixture else min(60, len(imported)), 'memories': len(memories), 'draft_characters': len(draft)}, 'credential_policy': 'reference only; original owner alone refreshes or changes account', 'source_database_modified': False, 'autostart': False, 'automatic_screen_observation': False, 'files': {str(path.relative_to(native)): digest(path) for path in native.rglob('*') if path.is_file()}}
    write(native / 'migration-receipt.json', receipt)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('database', 'accounts', 'roles', 'references', 'destination'): parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--public-fixture', action='store_true')
    args = parser.parse_args()
    receipt = migrate(args.database.resolve(), args.accounts.resolve(), args.roles.resolve(), json.loads(args.references.read_text()), args.destination.absolute(), public_fixture=args.public_fixture)
    print(json.dumps({'status': 'CREATED', 'destination': str(args.destination), 'version': VERSION, 'counts': receipt['counts']}))
