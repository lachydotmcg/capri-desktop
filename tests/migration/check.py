"""Migration preserves public fixture state without changing its source or account."""
import hashlib
import importlib.util
import json
import sqlite3
import tempfile
from pathlib import Path

spec = importlib.util.spec_from_file_location('migration', Path(__file__).resolve().parents[2] / 'tools/migrate-linux-profile.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
with tempfile.TemporaryDirectory(prefix='capri-migration-') as temporary:
    root = Path(temporary)
    account = 'a' * 64
    accounts = root / 'accounts'; accounts.mkdir(mode=0o700)
    (accounts / 'active.json').write_text(json.dumps({'account': account}))
    credential = accounts / (account + '.json'); credential.write_text('PRIVATE_FIXTURE_SENTINEL')
    database = root / 'companion.sqlite'
    with sqlite3.connect(database) as connection:
        connection.executescript('CREATE TABLE app_state(partition TEXT, key TEXT, value TEXT); CREATE TABLE turns(id INTEGER, partition TEXT, role TEXT, text TEXT, status TEXT, created TEXT); CREATE TABLE memories(id INTEGER,partition TEXT,text TEXT,source_turn INTEGER,created TEXT);')
        for partition, key, value in [('local-owner','profile',{'quiet': True}),('local-owner','presentation-v8',{'voice_volume':37,'voice_touched':True}), (account,'conversation-presentation-v8',{'memory_turns':24}), (account,'draft','public unsent draft'), (account,'desktop-observation-v5',{'enabled':True,'allow_input':False})]:
            connection.execute('INSERT INTO app_state VALUES(?,?,?)',(partition,key,json.dumps(value)))
        connection.executemany('INSERT INTO turns VALUES(?,?,?,?,?,?)',[(1,account,'user','hello','completed','fixture'),(2,account,'assistant','unfinished','streaming','fixture')])
    roles = root / 'roles.json'
    roles.write_text(json.dumps({'profiles':{hashlib.sha256(json.dumps([['chatgpt_plan',account],name],separators=(',',':')).encode()).hexdigest():{'model':'public-model','effort':'low','output_tokens':256} for name in ('chat','gui')}}))
    artifact = root / 'retained.json'; artifact.write_text('{}')
    references = {key:str(artifact) for key in ('plan_python','voice_python','voice_profile','asr_python','asr_manifest')}; references['microphone_node']=None
    before = database.read_bytes(); account_before = credential.read_bytes()
    destination = root / 'owner'
    receipt = module.migrate(database, accounts, roles, references, destination)
    native = destination / 'Capri'; settings = json.loads((native/'cross.json').read_text())
    assert database.read_bytes()==before and credential.read_bytes()==account_before, 'source state modified'
    assert settings['Silent'] and settings['NoCheckIns'] and not settings['Watch'], 'quiet or manual observation lost'
    assert settings['Volume']==37 and settings['CubicVoiceVolume'] and settings['Recall']==24, 'retained choices changed'
    assert len(settings['Permissions'])==11 and settings['Permissions'][5]=='0', 'input grant broadened'
    assert (native/'draft.txt').read_text()=='public unsent draft', 'draft dropped'
    assert len(json.loads((native/'chat.json').read_text())['messages'])==1, 'unfinished turn included'
    for path in native.rglob('*'):
        if path.is_file():
            assert path.stat().st_mode & 0o077 == 0, 'private export permission widened'
            assert b'PRIVATE_FIXTURE_SENTINEL' not in path.read_bytes(), 'account secret copied'
    try: module.migrate(database,accounts,roles,references,destination)
    except ValueError: pass
    else: raise AssertionError('existing destination overwritten')
    module.migrate(database, accounts, roles, references, root/'public',public_fixture=True)
    assert (root/'public/Capri/draft.txt').read_text()==''
    assert json.loads((root/'public/Capri/chat.json').read_text())['messages']==[]
    assert json.loads((native/'legacy-export-v1/preserved-state.json').read_text())['presentation']['voice_touched']
    print('PASS migration: source and credentials unchanged; choices, grants, privacy, draft, completed history, archive and create-only export')
