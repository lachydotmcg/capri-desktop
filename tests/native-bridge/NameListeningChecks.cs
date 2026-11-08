using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CapriDesktop;

internal static class NameListeningChecks
{
    private const string Fake = """
import json,sys
cfg=json.loads(sys.stdin.readline());session='public-fixture-session';generation=cfg['generation'];context=cfg['context'];serial=0

def emit(kind,**fields):print(json.dumps({'event':kind,'session':session,'generation':generation,**fields}),flush=True)
def final(utterance,text,gen=None):print(json.dumps({'event':'final','session':session,'generation':generation if gen is None else gen,'utterance':utterance,'text':text}),flush=True)
emit('listening',detector='protocol-fixture')
if cfg['manifest']=='wrong-session':
 print(json.dumps({'event':'final','session':'wrong','generation':generation,'utterance':'wrong','text':'Capri wrong session'}),flush=True)
else:
 if not context['barred'] and not context['speaking']:final('first','Capri public first')
 emit('state',mode='paused' if context['barred'] or context['speaking'] else 'chatting')
 for line in sys.stdin:
  update=json.loads(line);old=generation;generation=update['generation'];context=update['context'];serial+=1
  final('stale-'+str(serial),'Capri stale generation',old)
  if context['barred'] or context['speaking']:
   final('echo-'+str(serial),'Capri output echo')
   emit('state',mode='paused')
  else:
   final('fresh-'+str(serial),'Capri public fresh')
   final('fresh-'+str(serial),'Capri duplicate')
   emit('state',mode='chatting')
""";

    internal static void Run() { Task.Run(RunAsync).GetAwaiter().GetResult(); }
    private static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) { Console.WriteLine("LIMIT native name-listener process checks require Linux."); return; }
        int checks=0;
        void Check(bool value,string name) { if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
        string folder=Path.Combine(Path.GetTempPath(),"capri-name-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        string script=Path.Combine(folder,"worker.py");File.WriteAllText(script,Fake);
        var processes=new List<int>();
        Process Launch(string python,string manifest)
        {
            var info=new ProcessStartInfo("/usr/bin/python3") {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("-u");info.ArgumentList.Add(script);var p=Process.Start(info);processes.Add(p.Id);return p;
        }
        TaskCompletionSource<T> Signal<T>()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=Signal<NameTranscript>();var fresh=Signal<NameTranscript>();var paused=Signal<bool>();var resumed=Signal<bool>();
        var finals=new List<NameTranscript>();var errors=new List<string>();
        var initial=new NameListeningState("account|session|device","physical-node","fingerprint",false,false,false);
        var ears=new OwnedNameEars(Launch);
        Check(!ears.Active && processes.Count==0,"name-listener construction opens no microphone or child");
        Task running=ears.Start("unused", "fixture", initial,
            status=>{if(status=="Name listening paused.")paused.TrySetResult(true);if(status=="Chatting. No need to say Capri." && finals.Count>1)resumed.TrySetResult(true);},
            line=>{lock(finals){finals.Add(line);if(finals.Count==1)first.TrySetResult(line);else fresh.TrySetResult(line);}},
            error=>{lock(errors)errors.Add(error);});
        NameTranscript before=await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(ears.Active && ears.IsCurrent(before),"accepted transcript carries a live listening generation");
        ears.Update(initial with {Busy=true});
        Check(ears.IsCurrent(before),"thinking changes do not revoke the current listening authority");
        ears.GateForPlayback();
        Check(!ears.IsCurrent(before),"playback gate revokes queued text before child acknowledgement");
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock(finals)Check(finals.Count==1,"late and same-generation self-voice finals are discarded while gated");
        ears.Update(initial);
        NameTranscript after=await fresh.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(ears.IsCurrent(after) && !ears.IsCurrent(before),"a fresh post-playback capture gets distinct authority");
        lock(finals)Check(finals.Count==2,"duplicate utterance identity is delivered once");
        ears.Stop();
        Check(!ears.IsCurrent(after),"Stop revokes a queued accepted transcript immediately");
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!ears.Active && errors.Count==0,"Stop reaps the listener without reporting fake recognition failure");
        Check(!Directory.Exists("/proc/"+processes[0]),"Stop leaves no owned capture-supervisor child");

        foreach(string boundary in new[]{"account","session","microphone","fingerprint"})
        {
            var accepted=Signal<NameTranscript>();var e=new OwnedNameEars(Launch);
            Task job=e.Start("unused","fixture",initial,_=>{},line=>accepted.TrySetResult(line),_=>{});
            var line=await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var changed=boundary=="account" ? initial with {Identity="new-account"} : boundary=="session" ? initial with {Identity="new-session"} : boundary=="microphone" ? initial with {Source="new-node"} : initial with {Fingerprint="new-device"};
            e.Update(changed);Check(!e.IsCurrent(line),boundary+" change revokes the old transcript");
            await job.WaitAsync(TimeSpan.FromSeconds(5));Check(!e.Active,boundary+" change releases the owned worker");
        }
        var rejected=Signal<string>();var wrong=new OwnedNameEars(Launch);int invalid=0;
        Task wrongJob=wrong.Start("unused","wrong-session",initial,_=>{},_=>invalid++,error=>rejected.TrySetResult(error));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5));await wrongJob.WaitAsync(TimeSpan.FromSeconds(5));
        Check(invalid==0 && !wrong.Active,"foreign session messages stop the listener without submitting text");
        var blocked=Signal<bool>();var muted=new OwnedNameEars(Launch);int mutedFinals=0;
        Task mutedJob=muted.Start("unused","fixture",initial with {Barred=true},_=>blocked.TrySetResult(true),_=>mutedFinals++,_=>{});
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));Check(mutedFinals==0,"initial barred state submits no utterance");
        muted.Stop();await mutedJob.WaitAsync(TimeSpan.FromSeconds(5));
        var entered=Signal<bool>();using var release=new ManualResetEventSlim(false);int warmedFinals=0;
        var warming=new OwnedNameEars((python,manifest)=>{entered.TrySetResult(true);release.Wait();return Launch(python,manifest);});
        Task warmingJob=warming.Start("unused","fixture",initial,_=>{},_=>warmedFinals++,_=>{});
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));warming.Stop();release.Set();
        await warmingJob.WaitAsync(TimeSpan.FromSeconds(5));
        Check(warmedFinals==0 && !warming.Active,"Stop during warmup prevents a late listener or transcript");
        Check(processes.TrueForAll(pid=>!Directory.Exists("/proc/"+pid)),"all protocol-fixture children are reaped");
        Console.WriteLine("PASS name listener controller checks="+checks);
    }
}
