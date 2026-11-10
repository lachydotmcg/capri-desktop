#!/usr/bin/env python3
"""Deterministic production name-gate tests; no microphones, models or network."""
from pathlib import Path
import asyncio
import importlib.util
import math
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2] / 'cross' / 'bridge' / 'speech'
sys.path.insert(0, str(ROOT))
from name_chat import NameChat
from name_listen import NameListener, context, revision


class Clock:
    def __init__(self): self.at = 100.0
    def __call__(self): return self.at
    def advance(self, seconds): self.at += seconds


class GateTests(unittest.TestCase):
    def setUp(self): self.clock, self.gate = Clock(), None; self.gate = NameChat(self.clock)
    def say(self, text, identity='line', start=True):
        if start: self.gate.speech_started(identity, self.clock())
        return self.gate.final(identity, text)
    def wake(self):
        self.assertIsNotNone(self.say('Hey Capri', 'wake'))
        self.assertTrue(self.gate.open)
        self.assertEqual(self.gate.until - self.clock(), 20)
    def test_no_ambient_request_or_stop_leaves_gate(self):
        for i,text in enumerate(['what is next', 'stop', 'cancel please', 'Hey Jarvis', 'caprice', 'Capricorn', 'scapri']):
            self.assertIsNone(self.say(text,str(i)))
        self.assertFalse(self.gate.open)
    def test_actual_name_not_jarvis_opens_twenty_seconds(self):
        for name in ['Capri','hey CAPRI!','Ｃａｐｒｉ, open a terminal','can you help, Capri?']:
            gate=NameChat(self.clock);gate.speech_started(name,self.clock());line=gate.final(name,name)
            self.assertIsNotNone(line);self.assertTrue(line.named);self.assertEqual(gate.mode,'chatting')
            self.assertEqual(gate.until-self.clock(),20)
    def test_followup_at_nineteen_seconds_and_timer_restart(self):
        self.wake();self.clock.advance(19);line=self.say('what about tomorrow','next')
        self.assertEqual(line.text,'what about tomorrow');self.assertEqual(self.gate.until-self.clock(),20)
    def test_quiet_timeout_returns_name_only_without_unprefixed_stop(self):
        self.wake();self.clock.advance(20);self.gate.tick();self.assertEqual(self.gate.mode,'name_only')
        self.assertIsNone(self.say('stop','stop'));self.assertIsNone(self.say('what now','after'))
    def test_real_speech_start_before_deadline_survives_late_completion(self):
        self.wake();self.clock.advance(19.9);self.gate.speech_started('slow',self.clock());self.clock.advance(4)
        self.gate.tick();self.assertFalse(self.gate.open)
        self.assertEqual(self.gate.final('slow','this line began before expiry').text,'this line began before expiry')
        self.assertTrue(self.gate.open)
    def test_delayed_vad_start_uses_acoustic_boundary_not_callback_time(self):
        self.wake();self.clock.advance(20.3);self.gate.speech_started('vad',119.9)
        self.assertIsNotNone(self.gate.final('vad','started in time'))
    def test_utterance_started_at_or_after_deadline_does_not_count(self):
        self.wake();self.clock.advance(20)
        self.assertIsNone(self.say('too late','exact'));self.clock.advance(.1)
        self.assertIsNone(self.say('still late','late'))
    def test_pre_name_started_line_cannot_leak_after_wake(self):
        self.gate.speech_started('ambient',self.clock());self.clock.advance(.5);self.wake()
        self.assertIsNone(self.gate.final('ambient','private ambient conversation'))
    def test_missing_boundary_cannot_admit_old_nameless_final(self):
        self.wake();self.assertIsNone(self.say('a late decoder final','unknown',False))
    def test_thinking_holds_open_window_but_does_not_reopen_expired(self):
        self.wake();self.clock.advance(19);self.gate.control(barred=False,speaking=False,busy=True)
        self.clock.advance(10);self.gate.tick();self.assertEqual(self.gate.until-self.clock(),20)
        self.gate.control(barred=False,speaking=False,busy=False);self.clock.advance(21);self.gate.tick()
        self.assertFalse(self.gate.open);self.gate.control(barred=False,speaking=False,busy=True)
        self.assertFalse(self.gate.open)
    def test_self_voice_gate_discards_inflight_and_waits_for_clean_capture(self):
        self.wake();self.gate.speech_started('human',self.clock());self.gate.control(barred=False,speaking=True,busy=False)
        self.assertEqual(self.gate.mode,'paused');self.assertIsNone(self.gate.final('human','Capri said her own name'))
        self.clock.advance(5);self.gate.tick();self.gate.control(barred=False,speaking=False,busy=False)
        self.assertTrue(self.gate.open);self.assertIsNone(self.gate.final('lost-overlap','without fresh boundary'))
        self.assertIsNotNone(self.say('a fresh follow-up','clean'))
    def test_speech_only_extends_an_already_open_conversation(self):
        self.gate.control(barred=False,speaking=True,busy=False);self.clock.advance(3);self.gate.tick()
        self.gate.control(barred=False,speaking=False,busy=False);self.assertFalse(self.gate.open)
    def test_mute_sleep_lock_stop_clear_both_window_and_pending_boundaries(self):
        for reason in ['mute','sleep','lock','stop']:
            gate=NameChat(self.clock);gate.speech_started('wake',self.clock());gate.final('wake','Capri');gate.speech_started('pending',self.clock())
            gate.control(barred=True,speaking=False,busy=False)
            self.assertEqual(gate.mode,'paused',reason);self.assertIsNone(gate.final('pending','Capri request'))
            gate.control(barred=False,speaking=False,busy=False);self.assertFalse(gate.open)
            gate.speech_started('fresh',self.clock());self.assertIsNone(gate.final('fresh','resume without name'))
    def test_job_name_only_revokes_followups_and_ambient_stop(self):
        self.wake();self.gate.control(barred=False,speaking=False,busy=True,name_only=True)
        self.assertIsNone(self.say('stop','stop'));self.assertIsNone(self.say('do another thing','other'))
        self.assertIsNotNone(self.say('Capri stop','named-stop'));self.assertFalse(self.gate.open)
    def test_duplicates_do_not_reopen_or_double_submit(self):
        self.wake();self.clock.advance(21);self.assertIsNone(self.gate.final('wake','Capri'));self.assertFalse(self.gate.open)
    def test_internal_pending_and_completed_state_is_bounded(self):
        for i in range(1000):
            self.gate.speech_started(str(i),self.clock());self.gate.final(str(i),'ambient')
        self.assertLessEqual(len(self.gate.started),64);self.assertLessEqual(len(self.gate.finished),64)
        for i in range(1000):self.gate.speech_started('pending'+str(i),self.clock())
        self.assertLessEqual(len(self.gate.started),64)
    def test_invalid_transcripts_and_boundaries_fail_closed(self):
        for text in ['', ' '*3, 'x'*4097, None, 1]:
            with self.assertRaises(ValueError):self.gate.final('x',text)
        for began in [True, math.inf, math.nan, '1', None]:
            with self.assertRaises(ValueError):self.gate.speech_started('x',began)
    def test_control_schema_is_strict(self):
        valid={'barred':False,'speaking':False,'busy':False,'name_only':False}
        self.assertEqual(context(valid),valid)
        for value in [{},dict(valid,extra=True),dict(valid,busy=1)]:
            with self.assertRaises(ValueError):context(value)
        for value in [False,0,-1,1.5,2**54]:
            with self.assertRaises(ValueError):revision(value)


class WorkerControlTests(unittest.IsolatedAsyncioTestCase):
    @staticmethod
    async def verified(*args): pass
    async def test_control_during_decoder_warmup_is_not_published_before_listening(self):
        output=[];reader=asyncio.StreamReader();clock=Clock()
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
        listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=self.verified)
        reader.feed_data(b'{"command":"state","generation":2,"context":{"barred":false,"speaking":true,"busy":false,"name_only":false}}\n');reader.feed_eof()
        await listener.commands();self.assertTrue(listener.policy.blocked);self.assertEqual(output,[]);self.assertEqual(listener.authority,2)
    async def test_gate_requires_new_authority_and_never_accepts_old_context(self):
        output=[];reader=asyncio.StreamReader();clock=Clock()
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
        listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=self.verified)
        reader.feed_data(b'{"command":"state","generation":1,"context":{"barred":false,"speaking":true,"busy":false,"name_only":false}}\n');reader.feed_eof()
        with self.assertRaises(ValueError):await listener.commands()
    async def test_stale_control_cannot_reopen_newer_blocked_gate(self):
        output=[];reader=asyncio.StreamReader();clock=Clock()
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':2,'context':{'barred':True,'speaking':False,'busy':False,'name_only':False}}
        listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=self.verified);listener.advertised=True
        reader.feed_data(b'{"command":"state","generation":1,"context":{"barred":false,"speaking":false,"busy":false,"name_only":false}}\n');reader.feed_eof()
        await listener.commands();self.assertTrue(listener.policy.blocked);self.assertEqual(output,[])

    async def test_only_name_accepted_finals_leave_worker(self):
        for phrase, expected in [('ambient conversation',False),('Hey Jarvis',False),('Capri, help please',True)]:
            output=[];reader=asyncio.StreamReader();clock=Clock()
            cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
            listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=self.verified);listener.advertised=True
            class PCM:
                async def readexactly(self,n):return bytes(n)
            class Capture:stdout=PCM()
            class Decoder:
                samples=0
                async def send(self,*args,**kwargs):
                    self.samples=1280;listener.shutdown.set()
                    return [{'event':'speech_started','utterance_id':'test','speech_start_sample':0},{'event':'asr_partial','utterance_id':'test','text':phrase},{'event':'asr_final','utterance_id':'test','speech_start_sample':0,'text':phrase}]
            listener.capture=Capture();listener.decoder=Decoder();listener.capture_start=clock();listener.capture_ready.set()
            await listener.audio();final=[e for e in output if e['event']=='final']
            self.assertEqual(len(final),int(expected))
            if expected:self.assertEqual(final[0]['text'],phrase)
            else:self.assertFalse(any(phrase in str(e) for e in output))
    async def test_decoder_finishing_after_playback_gate_cannot_export_a_name(self):
        output=[];reader=asyncio.StreamReader();clock=Clock()
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
        listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=self.verified);listener.advertised=True
        class PCM:
            async def readexactly(self,n):return bytes(n)
        class Capture:stdout=PCM()
        class Decoder:
            samples=0
            async def send(self,*args,**kwargs):
                self.samples=1280;listener.authority=2;listener.policy.control(barred=False,speaking=True,busy=False);listener.shutdown.set()
                return [{'event':'speech_started','utterance_id':'old','speech_start_sample':0},{'event':'asr_final','utterance_id':'old','speech_start_sample':0,'text':'Capri from output audio'}]
        listener.capture=Capture();listener.decoder=Decoder();listener.capture_start=clock();listener.capture_ready.set()
        await listener.audio();self.assertFalse(any(e['event']=='final' for e in output))

    async def test_device_replacement_before_acceptance_exports_nothing(self):
        output=[];reader=asyncio.StreamReader();clock=Clock();checks=[]
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
        async def changed_device(*args):checks.append(args);raise ValueError('Selected physical microphone is no longer available')
        listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=changed_device);listener.advertised=True
        class PCM:
            async def readexactly(self,n):return bytes(n)
        class Capture:stdout=PCM()
        class Decoder:
            samples=0
            async def send(self,*args,**kwargs):
                self.samples=1280;listener.shutdown.set()
                return [{'event':'speech_started','utterance_id':'test','speech_start_sample':0},{'event':'asr_final','utterance_id':'test','speech_start_sample':0,'text':'Capri private input'}]
        listener.capture=Capture();listener.decoder=Decoder();listener.capture_start=clock();listener.capture_ready.set()
        with self.assertRaises(ValueError):await listener.audio()
        self.assertEqual(len(checks),1);self.assertFalse(any(e['event']=='final' for e in output))
    async def test_gate_change_during_final_device_check_prevents_export(self):
        output=[];reader=asyncio.StreamReader();clock=Clock()
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
        async def gate_during_check(*args):listener.authority=2;listener.policy.control(barred=False,speaking=True,busy=False)
        listener=NameListener(cfg,reader,clock=clock,write=output.append,microphone_check=gate_during_check);listener.advertised=True
        class PCM:
            async def readexactly(self,n):return bytes(n)
        class Capture:stdout=PCM()
        class Decoder:
            samples=0
            async def send(self,*args,**kwargs):
                self.samples=1280;listener.shutdown.set()
                return [{'event':'speech_started','utterance_id':'test','speech_start_sample':0},{'event':'asr_final','utterance_id':'test','speech_start_sample':0,'text':'Capri private input'}]
        listener.capture=Capture();listener.decoder=Decoder();listener.capture_start=clock();listener.capture_ready.set()
        await listener.audio();self.assertFalse(any(e['event']=='final' for e in output))


class WorkerLifetimeTests(unittest.IsolatedAsyncioTestCase):
    class Child:
        def __init__(self, decoder=False):
            self.returncode=None;self.stdout=asyncio.StreamReader();self.exit=asyncio.Event();self.requests=[];self.samples=0;self.session=None;self.decoder=decoder
            parent=self
            class Input:
                def write(self,data):
                    cmd=__import__('json').loads(data);parent.requests.append(cmd)
                    if cmd['command']=='start':parent.session=cmd['session_id']
                    if cmd['command']=='audio':
                        parent.samples+=1280
                        if parent.samples==1280:
                            for e in [{'event':'speech_started','speech_start_sample':0},{'event':'asr_final','speech_start_sample':0,'text':'Capri public fixture'}]:
                                e.update(session_id=parent.session,utterance_id='public');parent.stdout.feed_data((__import__('json').dumps(e)+'\n').encode())
                    parent.stdout.feed_data((__import__('json').dumps({'event':'ack','sequence':cmd['sequence'],'samples':parent.samples})+'\n').encode())
                async def drain(self):pass
            self.stdin=Input()
            if decoder:self.stdout.feed_data(b'{"event":"ready"}\n')
            else:self.stdout.feed_data(bytes(2560))
        def terminate(self):
            if self.returncode is None:self.returncode=0;self.stdout.feed_eof();self.exit.set()
        def kill(self):self.terminate()
        async def wait(self):await self.exit.wait();return self.returncode
    async def test_eof_during_warmup_never_opens_microphone(self):
        reader=asyncio.StreamReader();reader.feed_eof();output=[];children=[]
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':False,'busy':False,'name_only':False}}
        async def verified(*args):pass
        async def launch(*args,**kwargs):
            child=self.Child(decoder=True);children.append((args[0],child));await asyncio.sleep(0);return child
        with patch('name_listen.asyncio.create_subprocess_exec',side_effect=launch):
            await NameListener(cfg,reader,write=output.append,microphone_check=verified).run()
        self.assertEqual(len(children),1);self.assertNotEqual(children[0][0],'pw-record');self.assertTrue(all(c.returncode is not None for _,c in children));self.assertFalse(any(e['event']=='final' for e in output))
    async def test_full_local_protocol_gates_audio_and_reaps_children(self):
        reader=asyncio.StreamReader();output=[];children=[]
        cfg={'source':'physical-node','fingerprint':'pinned','manifest':'manifest','generation':1,'context':{'barred':False,'speaking':True,'busy':False,'name_only':False}}
        async def verified(*args):pass
        async def launch(*args,**kwargs):
            child=self.Child(decoder=args[0]!='pw-record');children.append((args[0],child));return child
        def emit(event):
            output.append(event)
            if event['event']=='state' and event['mode']=='paused' and event['generation']==1:
                self.assertFalse(any(tool=='pw-record' for tool,_ in children))
                reader.feed_data(b'{"command":"state","generation":2,"context":{"barred":false,"speaking":false,"busy":false,"name_only":false}}\n')
            if event['event']=='final':reader.feed_eof()
        with patch('name_listen.asyncio.create_subprocess_exec',side_effect=launch):
            await asyncio.wait_for(NameListener(cfg,reader,write=emit,microphone_check=verified).run(),2)
        self.assertEqual([e['text'] for e in output if e['event']=='final'],['Capri public fixture'])
        self.assertEqual(sum(tool=='pw-record' for tool,_ in children),1)
        self.assertTrue(all(c.returncode is not None for _,c in children))
        decoder=next(c for tool,c in children if tool!='pw-record');self.assertFalse(next(c for c in decoder.requests if c['command']=='start')['wake'])


if __name__=='__main__':unittest.main(verbosity=2)
