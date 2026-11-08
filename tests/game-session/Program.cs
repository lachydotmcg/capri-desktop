using CapriDesktop;
using System.Text.Json;

internal static class Program
{
    private static int count;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private static bool Reject(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
    private sealed class Desktop : IComputerDesktop
    {
        internal ComputerSnapshot Current = new("session", "layout", new("0x123",123,"birth","fixture",new(0,0,100,100),1,1,false),new("OUT",new(0,0,100,100),1,0,100,100),10,10,true,1,1);
        internal int Captures; internal bool SamePixels;internal Action Snapshotting;
        public ComputerSnapshot Snapshot(string output = null, bool focus = false) { Snapshotting?.Invoke();return Current; }
        public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token) => new(){Source=source,Area=source.Target.Area,Width=100,Height=100,Png=BitConverter.GetBytes(SamePixels?0:++Captures),Jpeg=new byte[]{1}};
        public void Move(int x,int y,CancellationToken token) => Current=Current with{PointerX=x,PointerY=y};
    }
    private sealed class Input : IComputerInput
    {
        internal int Actions; internal Action<ComputerAction,CancellationToken> During;
        public void Execute(ComputerAction a,ComputerFrame f,IComputerDesktop d,Action<double,double> guard,CancellationToken token,Action<double,double> completed=null)
        { guard(f.Source.PointerX,f.Source.PointerY);Actions++;During?.Invoke(a,token);(completed??guard)(f.Source.PointerX,f.Source.PointerY); }
    }
    private static string Action(string frame,string action="click",string previous="verified",string outcome=null,string transition=null) => JsonSerializer.Serialize(new{action,frame,previous_effect=previous,label="public fixture move",risk="none",secret=false,x=10,y=10,button="left",round_result=outcome,transition});
    private static (ComputerRunner Runner,ComputerJob Job,Desktop Desktop,Input Input) Setup(ComputerGameSession policy=null)
    {
        var d=new Desktop();var i=new Input();var j=new ComputerJob{Task="play fixture",Asked="play fixture",Game=policy??new()};
        var r=new ComputerRunner{Desktop=d,Input=i,Account=()=>"account",Think=(jpeg,prompt,instruction)=>throw new Exception("Set a proposal")};return(r,j,d,i);
    }
    public static void Main()
    {
        Security.Pick(Security.Trusted);
        foreach(string request in new[]{"play Mines","please play SuperTuxKart until I stop","play Mines for one round","play Mines for two minutes"})Check(ComputerGameSession.FromRequest(request,request)!=null,"explicit native game request: "+request);
        foreach(string request in new[]{"open Mines","play a song on Spotify","solve this chess puzzle","change game settings","what game is this?"})Check(ComputerGameSession.FromRequest(request,request)==null,"non-play request does not start a game session: "+request);
        var goal=ComputerGameSession.FromRequest("play Mines for one round","play Mines until one win");Check(goal.RoundGoal==0&&goal.UntilWin,"ending comes from exact owner words rather than invented model task");
        Check(Reject(()=>ComputerGameSession.FromRequest("play Mines","play Mines for 999999999999999999999 rounds")),"oversized ending rejected at boundary");
        Check(ComputerGameSession.FromRequest("play Mines","play Mines for 2 minutes").Duration==TimeSpan.FromMinutes(2),"requested duration is retained accurately");
        goal=new(1);Check(!goal.ObserveEnd("win",0)&&goal.Rounds==0,"old end screen with no delivered input cannot finish a requested round");Check(goal.ObserveEnd("loss",1)&&goal.Rounds==1,"one-round request ends only after played input and fresh ending assessment");Check(!goal.ObserveEnd("win",1)&&goal.Rounds==1,"same completed round cannot be counted twice");
        goal=new(0,true);Check(!goal.ObserveEnd("loss",1)&&goal.ObserveEnd("win",2),"win request continues after loss then finishes on assessed win");
        long start=System.Diagnostics.Stopwatch.GetTimestamp();goal=new(duration:TimeSpan.FromSeconds(2));goal.Begin(start);Check(!goal.TimeEnded(start+System.Diagnostics.Stopwatch.Frequency)&&goal.TimeEnded(start+2*System.Diagnostics.Stopwatch.Frequency),"duration uses the monotonic session clock");
        var apps=new[]{new InstalledApp("Steam","steam"),new InstalledApp("Mines","mines"),new InstalledApp("Terminal","terminal")};Check(ComputerGameLauncher.Resolve(apps,"play Mines on Steam","play Mines on Steam").Id=="mines","real game resolves instead of platform launcher");Check(ComputerGameLauncher.Resolve(apps,"play a game","play a game")==null,"unknown game never silently binds an unrelated recent app");
        {
            var(r,j,d,i)=Setup(new(1));int opened=0;bool focused=false;var launcher=new ComputerGameLauncher{Catalog=()=>apps.ToList(),Windows=()=>opened==0?new():new(){new(new IntPtr(0x123),"Mines","mines")},Launch=id=>{Check(id=="mines","launcher uses the installed identifier");opened++;},Focus=w=>{focused=true;return true;}};j.Task=j.Asked="play Mines for one round";j.Account="account";j.Permissions=Security.Saved;j.Binding=launcher.Prepare(j,d,()=>{},CancellationToken.None);Check(opened==1&&focused&&j.Binding.Target.Pid==123,"launch waits for and binds the actual requested game window");
            opened=0;launcher.MayOpen=()=>false;Check(Reject(()=>launcher.Prepare(j,d,()=>{},CancellationToken.None))&&opened==0,"opening permission is not bypassed by a game session");
        }
        {
            var(r,j,d,i)=Setup(new(1));r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),i.Actions==0?"click":"done",i.Actions==0?"none":"verified",i.Actions==0?null:"win");};string result=ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==1&&j.Game.Rounds==1&&result.Contains("requested game-session ending"),"one game round runs and completes through actual runner turns");
        }
        {
            var(r,j,d,i)=Setup(new(0,true));int round=0;r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);bool done=i.Actions>round;if(done)round++;return Action(json.RootElement.GetProperty("frame").GetString(),done?"done":"click",i.Actions==0?"none":"verified",done?(round==1?"loss":"win"):null);};ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==2&&j.Game.Rounds==2&&j.Game.Wins==1,"requested win continues across a lost round");
        }
        {
            var(r,j,d,i)=Setup();using var cancel=new CancellationTokenSource();r.MostSteps=2;r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);Check(json.RootElement.GetProperty("exact_request").GetString()==j.Asked,"original owner request survives every bounded slice");if(i.Actions==40){cancel.Cancel();cancel.Token.ThrowIfCancellationRequested();}return Action(json.RootElement.GetProperty("frame").GetString(),previous:i.Actions==0?"none":"verified");};bool stopped=false;try{ComputerJobs.RunGame(r,j,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped&&i.Actions==40&&j.Steps.Count==16&&j.Delivered.Count==40,"ongoing session crosses action slices with bounded history and retained delivery records");
        }
        {
            var(r,j,d,i)=Setup();r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),"done","none","win");};string result=ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==0&&j.Game.Rounds==0&&result.Contains("did not establish progress"),"repeated old ending assessments stop without invented play or round counts");
        }
        {
            var(r,j,d,i)=Setup(new(1));d.SamePixels=true;r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),previous:i.Actions==0?"none":"uncertain");};string result=ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==1&&result.Contains("uncertain")&&j.Delivered.Count==1,"uncertain effect ends game without action replay");r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString());};Check(Reject(()=>ComputerJobs.RunGame(r,j,CancellationToken.None))&&i.Actions==1,"retained fingerprint rejects repeating the same image/action after uncertain stop");
        }
        {
            var(r,j,d,i)=Setup(new(1));var old=d.Current;i.During=(a,t)=>d.Current=d.Current with{Target=d.Current.Target with{Address="0x456",Area=new(0,0,100,100)},FocusEpoch=2};r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),i.Actions==0?"click":"done",i.Actions==0?"none":"verified",i.Actions==0?null:"win",i.Actions==0?"same_process":null);};ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==1&&j.Binding.Target.Address=="0x456","declared same-process transition rebinds only before a fresh verification turn");Check(Reject(()=>ComputerJobs.Same(old,d.Current,true)),"strict global action guard still rejects the same focus/target change");
        }
        foreach(string change in new[]{"pid","birth","app","layout","output","backend"})
        {
            var(r,j,d,i)=Setup(new(1));i.During=(a,t)=>d.Current=change switch{"pid"=>d.Current with{Target=d.Current.Target with{Pid=456}},"birth"=>d.Current with{Target=d.Current.Target with{Birth="changed"}},"app"=>d.Current with{Target=d.Current.Target with{App="other"}},"layout"=>d.Current with{Layout="other"},"output"=>d.Current with{Output=d.Current.Output with{Name="OTHER"}},_=>d.Current with{Target=d.Current.Target with{XWayland=true}}};r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),previous:i.Actions==0?"none":"verified",transition:"same_process");};Check(Reject(()=>ComputerJobs.RunGame(r,j,CancellationToken.None))&&i.Actions==1,"declared transition cannot widen "+change+" authority");
        }
        {
            var(r,j,d,i)=Setup();r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);d.Current=d.Current with{FocusEpoch=2};return Action(json.RootElement.GetProperty("frame").GetString(),previous:"none",transition:"same_process");};Check(Reject(()=>ComputerJobs.RunGame(r,j,CancellationToken.None))&&i.Actions==0,"mid-inference focus revocation rejects input despite declared future transition");
        }
        {
            var(r,j,d,i)=Setup();i.During=(a,t)=>d.Current=d.Current with{PointerX=50};r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),previous:i.Actions==0?"none":"verified");};Check(Reject(()=>ComputerJobs.RunGame(r,j,CancellationToken.None))&&i.Actions==1,"pointer takeover between game turns cancels before next input");
        }
        {
            var(r,j,d,i)=Setup();j.Delivered.UnionWith(Enumerable.Range(0,ComputerGameSession.DeliveryLimit).Select(x=>x.ToString()));r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),previous:"none");};string result=ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==0&&j.Delivered.Count==ComputerGameSession.DeliveryLimit&&result.Contains("safety capacity"),"bounded session capacity preserves records and reports a stop rather than fake completion");
        }
        {
            var(r,j,d,i)=Setup();using var cancel=new CancellationTokenSource();using var started=new ManualResetEventSlim();int aborted=0;r.CancelInference=()=>Interlocked.Increment(ref aborted);r.Think=(b,p,q)=>{started.Set();cancel.Token.WaitHandle.WaitOne();cancel.Token.ThrowIfCancellationRequested();return "";};var work=Task.Run(()=>{try{ComputerJobs.RunGame(r,j,cancel.Token);}catch(OperationCanceledException){}});Check(started.Wait(2000),"thinking fixture started");cancel.Cancel();Check(work.Wait(2000)&&i.Actions==0&&aborted>0,"Stop cancels a thinking turn before input");
        }
        {
            var(r,j,d,i)=Setup();using var cancel=new CancellationTokenSource();using var started=new ManualResetEventSlim();bool held=false;i.During=(a,t)=>{held=true;started.Set();try{t.WaitHandle.WaitOne();t.ThrowIfCancellationRequested();}finally{held=false;}};r.Think=(b,p,q)=>{using var json=JsonDocument.Parse(q);return Action(json.RootElement.GetProperty("frame").GetString(),previous:"none");};var work=Task.Run(()=>{try{ComputerJobs.RunGame(r,j,cancel.Token);}catch(OperationCanceledException){}});Check(started.Wait(2000),"acting fixture started");cancel.Cancel();Check(work.Wait(2000)&&i.Actions==1&&!held,"Stop propagates through acting turn and its held-input cleanup");
        }
        Check(NativeGameEscape.Start(new Desktop().Current,()=>{},CancellationToken.None)==null,"native Wayland does not advertise an XWayland Escape observer");
        SelectionChecks();DeliveryCompletionChecks();SchemaChecks();VoiceChecks();Console.WriteLine("PASS game/session/recovery checks="+count);
    }
    private sealed class Provider : IBrainProvider
    {
        internal Action During;internal int Calls,Proposals=1,Epoch;internal string Account="public-recovery-fixture";
        public string Identity=>Account;public int Generation=>Epoch;public void Cancel(){}
        public string Complete(string model,List<ChatMessage> messages,string tools,List<KeyValuePair<string,ToolArguments>> calls,int limit,Action<string> streamed,string purpose,string parts=null){Calls++;During?.Invoke();for(int i=0;i<Proposals;i++)calls.Add(new("open_app",new(){Name="Mines"}));return "";}
    }
    private static void VoiceChecks()
    {
        var manager=new VoiceActionRecovery();long ticket=manager.Begin();using(manager.EnterPrimary(ticket)){Check(VoiceActionRecovery.AllowCurrentTool(),"current primary voice tool is admitted");}Check(!manager.TryClaimRecovery(ticket,"open Mines","I'll open Mines."),"a dispatched primary tool suppresses promised-action recovery");
        ticket=manager.Begin();Check(!manager.TryClaimRecovery(ticket,"hello","Hello there."),"conversation does not start action recovery");Check(manager.TryClaimRecovery(ticket,"open Mines","I'll open Mines."),"a genuine owner action promise with no tool claims one recovery");Check(!manager.TryClaimRecovery(ticket,"open Mines","I'll open Mines."),"recovery attempt cannot be claimed twice");using(manager.EnterPrimary(ticket)){Check(!VoiceActionRecovery.AllowCurrentTool(),"late original tool cannot race the claimed recovery");}using(manager.EnterRecovery(ticket)){Check(VoiceActionRecovery.AllowCurrentTool(),"claimed recovery path may dispatch");}manager.Cancel();using(manager.EnterRecovery(ticket)){Check(!VoiceActionRecovery.AllowCurrentTool(),"Stop invalidates delayed recovery tools");}
        ticket=manager.Begin();using(manager.EnterPrimary(ticket)){manager.End(ticket);Check(!VoiceActionRecovery.AllowCurrentTool(),"completed voice request rejects late primary callback");}
        ticket=manager.Begin();var provider=new Provider();var brain=new Brain{Model="fixture",Provider=provider};int actual=0;brain.Act=(tool,args)=>{if(!VoiceActionRecovery.AllowCurrentTool())return "ended";actual++;Check(args.Asked=="open Mines"&&args.Current(),"recovered tool retains actual owner request and current-generation guard");return "Observed fixture tool result.";};string result=manager.Recover(brain,ticket,"open Mines","I'll open Mines.");Check(actual==1&&provider.Calls==1&&result=="Observed fixture tool result.","second look reaches actual executor once and returns its result");
        ticket=manager.Begin();provider=new Provider{Proposals=0};brain=new Brain{Model="fixture",Provider=provider};brain.Act=(tool,args)=>throw new Exception("No tool expected");result=manager.Recover(brain,ticket,"open Mines","I'll open Mines.");Check(result==Hands.KeptNothing,"no proposed recovery tool becomes an explicit not-done result");
        ticket=manager.Begin();provider=new Provider{During=()=>manager.Cancel()};brain=new Brain{Model="fixture",Provider=provider};actual=0;brain.Act=(tool,args)=>{actual++;return "bad";};bool cancelled=false;try{manager.Recover(brain,ticket,"open Mines","I'll open Mines.");}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&actual==0,"cancellation while recovery thinks prevents late dispatch");
        ticket=manager.Begin();provider=new Provider{Proposals=9};brain=new Brain{Model="fixture",Provider=provider};brain.Act=(tool,args)=>throw new Exception("No oversized batch expected");result=manager.Recover(brain,ticket,"open Mines","I'll open Mines.");Check(result.Contains("bounded limit"),"recovery proposal batch is bounded before executing anything");
        ticket=manager.Begin();var old=ticket;using var allow=new ManualResetEventSlim();Task<bool> late;using(manager.EnterPrimary(old)){late=Task.Run(()=>{allow.Wait();return VoiceActionRecovery.AllowCurrentTool();});}Check(manager.TryClaimRecovery(ticket,"open Mines","I'll open Mines."),"recovery wins before a delayed primary dispatch");allow.Set();Check(late.Wait(2000)&&!late.Result,"AsyncLocal keeps late callback on its original rejected dispatch path");
        ticket=manager.Begin();long next;using(manager.EnterPrimary(ticket)){next=manager.Begin();Check(!VoiceActionRecovery.AllowCurrentTool(),"a new utterance invalidates the previous generation");}using(manager.EnterPrimary(next)){Check(VoiceActionRecovery.AllowCurrentTool(),"the new utterance retains its own dispatch path");}
        foreach(string change in new[]{"provider","identity","generation","model"})
        {
            ticket=manager.Begin();provider=new Provider();brain=new Brain{Model="fixture",Provider=provider};
            using(manager.EnterPrimary(ticket,brain))
            {
                Func<bool> guard=VoiceActionRecovery.DispatchGuard;Check(guard(),"voice execution captures current "+change+" authority");
                if(change=="provider")brain.Provider=new Provider();else if(change=="identity")provider.Account="different-account";else if(change=="generation")provider.Epoch++;else brain.Model="different-model";
                Check(!manager.Current(ticket)&&!guard()&&!VoiceActionRecovery.AllowCurrentTool()&&!manager.TryClaimRecovery(ticket,"open Mines","I'll open Mines."),"changed "+change+" cannot execute or recover the prior voice request");
            }
        }
    }

    private static void SchemaChecks()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"bridge","computer-action-format.json")));var schema=json.RootElement.GetProperty("schema");var required=schema.GetProperty("required").EnumerateArray().Select(x=>x.GetString()).ToHashSet();var properties=schema.GetProperty("properties");
        Check(json.RootElement.GetProperty("strict").GetBoolean()&&!schema.GetProperty("additionalProperties").GetBoolean()&&required.Contains("round_result")&&required.Contains("transition")&&properties.EnumerateObject().All(p=>required.Contains(p.Name)),"production model schema keeps strict fields including game assessment and transition");
        Check(properties.GetProperty("action").GetProperty("enum").EnumerateArray().Any(x=>x.GetString()=="wait")&&properties.GetProperty("transition").GetProperty("enum").EnumerateArray().Any(x=>x.GetString()=="same_process"),"production schema actually exposes fresh observation and same-process transition fields");
        var(r,j,d,i)=Setup();var frame=d.Capture(d.Current,true,CancellationToken.None);string old=Action(frame.Id,"done","none");using var oldJson=JsonDocument.Parse(old);var legacy=oldJson.RootElement.EnumerateObject().Where(p=>p.Name!="round_result"&&p.Name!="transition").ToDictionary(p=>p.Name,p=>p.Value.Clone());
        Check(ComputerAction.Parse(JsonSerializer.Serialize(legacy),frame).RoundResult==null,"legacy native fixtures remain parse-compatible without game fields");
        Check(Reject(()=>ComputerAction.Parse(Action(frame.Id,"click","none","win"),frame)),"round result cannot attach to delivered input");
        Check(Reject(()=>ComputerAction.Parse(Action(frame.Id,"wait","none",transition:"same_process"),frame)),"observation-only wait cannot authorize a window transition");
        j.Game=null;r.Think=(b,p,q)=>{using var proposal=JsonDocument.Parse(q);return Action(proposal.RootElement.GetProperty("frame").GetString(),"done","none","win");};Check(Reject(()=>r.Run(j,CancellationToken.None))&&i.Actions==0,"game-only metadata is rejected outside an explicitly requested game session");
        (r,j,d,i)=Setup();j.Game.Begin(System.Diagnostics.Stopwatch.GetTimestamp()-31*System.Diagnostics.Stopwatch.Frequency);r.Think=(b,p,q)=>{using var proposal=JsonDocument.Parse(q);return Action(proposal.RootElement.GetProperty("frame").GetString(),"wait","none");};string stopped=ComputerJobs.RunGame(r,j,CancellationToken.None);Check(i.Actions==0&&stopped.Contains("no input progress"),"repeated observation waits stop after the bounded no-progress interval without input");
        (r,j,d,i)=Setup(new(duration:TimeSpan.FromMilliseconds(100)));using var began=new ManualResetEventSlim();using var aborted=new ManualResetEventSlim();r.CancelInference=()=>aborted.Set();r.Think=(b,p,q)=>{began.Set();if(!aborted.Wait(2000))throw new Exception("Duration did not cancel inference");throw new OperationCanceledException();};string elapsed=ComputerJobs.RunGame(r,j,CancellationToken.None);Check(began.IsSet&&aborted.IsSet&&i.Actions==0&&elapsed.Contains("elapsed during a turn"),"requested duration cancels inference and reports unverified final effects without input replay");
    }

    private static void SelectionChecks()
    {
        Func<OpenWindow,bool> previous=Hands.Gamey;
        try
        {
            Hands.Gamey=w=>w.Program=="org.gnome.Mines";
            var(r,j,d,i)=Setup();int focuses=0;var launcher=new ComputerGameLauncher{Catalog=()=>new(),Windows=()=>new(){new(new IntPtr(0x123),"Mines","org.gnome.Mines")},Focus=w=>{focuses++;return true;},Launch=id=>throw new Exception("Unknown app must not launch")};
            j.Task=j.Asked="play TotallyUnknownGame";
            Check(Reject(()=>launcher.Prepare(j,d,()=>{},CancellationToken.None))&&focuses==0,"Prepare rejects unknown named game instead of selecting the unique unrelated Mines window");
            j.Task="play Mines";j.Asked="play TotallyUnknownGame";
            Check(Reject(()=>launcher.Prepare(j,d,()=>{},CancellationToken.None))&&focuses==0,"model task cannot substitute another game for the exact owner request");
            j.Task=j.Asked="play Mines";Check(launcher.Prepare(j,d,()=>{},CancellationToken.None).Target.Pid==123&&focuses==1,"Prepare binds a game named by owner words even when its catalog entry is absent");
            j.Task=j.Asked="play the current game";Check(launcher.Prepare(j,d,()=>{},CancellationToken.None).Target.Pid==123&&focuses==2,"explicit current-game request may bind the unique identified open game");
            j.Task=j.Asked="play a game for one round";Check(launcher.Prepare(j,d,()=>{},CancellationToken.None).Target.Pid==123&&focuses==3,"explicit unspecified-game request retains its bounded ending");
        }
        finally { Hands.Gamey=previous; }
    }

    private static void DeliveryCompletionChecks()
    {
        // The child uses the real ArchInput READY/G/exit path. Its marker and
        // permit handshake places a target change after input admission but
        // before confirmed process exit, without desktop input or timing sleeps.
        foreach(string change in new[]{"resize","new_window","focus_back","cross_process","late_resize","unverified","session","layout","output","observation","request","account","permission","pointer","mid_drag"})
        {
            Security.Pick(Security.Trusted);
            string dir=Path.Combine(Path.GetTempPath(),"capri-input-completion-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string marker=Path.Combine(dir,"delivered"),script=Path.Combine(dir,"helper.py");
            File.WriteAllText(script,"import sys, pathlib\nn=int(sys.stdin.buffer.readline())\nsys.stdin.buffer.read(n)\nprint('READY',flush=True)\nassert sys.stdin.buffer.read(1)==b'G'\npathlib.Path(sys.argv[1]).write_text('delivered')\nassert sys.stdin.buffer.read(1)==b'X'\n"+(change=="mid_drag"?"print('STEP 1',flush=True)\nsys.stdin.buffer.read(1)\n":""));
            var(r,j,d,i)=Setup(new(1));System.Diagnostics.Process helper=null;using var changed=new ManualResetEventSlim();Exception watcherError=null;
            using var watcher=new FileSystemWatcher(dir,"delivered");watcher.Created+=(s,e)=>
            {
                try
                {
                    var before=d.Current;
                    d.Current=change switch
                    {
                        "new_window"=>before with{Target=before.Target with{Address="0x456"},FocusEpoch=before.FocusEpoch+1},
                        "focus_back"=>before with{Target=before.Target with{Area=new(0,0,120,100)},FocusEpoch=before.FocusEpoch+2},
                        "cross_process"=>before with{Target=before.Target with{Pid=456}},
                        "session"=>before with{Session="other-session"},
                        "layout"=>before with{Layout="other-layout"},
                        "output"=>before with{Output=before.Output with{Name="OTHER"}},
                        "observation"=>before with{ObservationEpoch=before.ObservationEpoch+1},
                        "pointer"=>before with{PointerX=50},
                        _=>before with{Target=before.Target with{Area=new(0,0,120,100)}}
                    };
                    if(change=="request")j.Current=()=>false;
                    if(change=="account")r.Account=()=>"other-account";
                    if(change=="permission")Security.Set(Security.Open,false);
                    if(change=="late_resize")
                    {
                        int snapshots=0;d.Snapshotting=()=>{if(++snapshots==2){d.Current=d.Current with{Target=d.Current.Target with{Area=new(0,0,130,100)}};d.Snapshotting=null;}};
                    }
                    helper.StandardInput.Write("X");helper.StandardInput.Flush();
                }
                catch(Exception error){watcherError=error;}
                finally{changed.Set();}
            };watcher.EnableRaisingEvents=true;
            try
            {
                int starts=0;r.Input=new ArchInput(script,start=>{starts++;var command=ComputerProcess.Start("/usr/bin/python3",new[]{"-u",script,marker});helper=System.Diagnostics.Process.Start(command);return helper;});
                int observations=0;r.Think=(b,p,q)=>
                {
                    using var json=JsonDocument.Parse(q);observations++;
                    if(observations==1&&change=="mid_drag")return JsonSerializer.Serialize(new{action="drag",frame=json.RootElement.GetProperty("frame").GetString(),previous_effect="none",label="public transition",risk="none",secret=false,x=10,y=10,end_x=20,end_y=10,duration=200,round_result="none",transition="same_process"});
                    return Action(json.RootElement.GetProperty("frame").GetString(),observations==1?"click":"done",observations==1?"none":change=="unverified"?"uncertain":"verified",observations==1?null:"win",observations==1?"same_process":null);
                };
                bool rejected=false;string result=null;try{result=ComputerJobs.RunGame(r,j,CancellationToken.None);}catch(Exception error)when(error is InvalidOperationException||error is OperationCanceledException){rejected=true;}
                Check(changed.Wait(2000)&&watcherError==null&&starts==1&&j.Delivered.Count==1,"production input helper admits exactly one nonreplayed attempt for "+change);
                if(change=="resize"||change=="new_window")Check(!rejected&&j.TotalSteps==1&&j.Game.Rounds==1&&observations==2,"confirmed "+change+" transition reaches one fresh verified ending through real ArchInput completion");
                else if(change=="unverified")Check(!rejected&&j.TotalSteps==1&&j.Game.Rounds==0&&result.Contains("uncertain"),"post-transition fresh observation must verify prior input before another action");
                else Check(rejected&&j.Game.Rounds==0,"completion rejects "+change+" and never replays confirmed input");
            }
            finally { watcher.EnableRaisingEvents=false;helper?.Dispose();Directory.Delete(dir,true);Security.Pick(Security.Trusted); }
        }
        // A failed guard immediately before G still wins; the completion seam
        // cannot excuse a mid-input revocation or call itself early.
        {
            string helperPath=Path.GetTempFileName();int guards=0,completions=0,pid=0;System.Diagnostics.Process process=null;
            try
            {
                File.WriteAllText(helperPath,"import sys\nn=int(sys.stdin.buffer.readline());sys.stdin.buffer.read(n)\nprint('READY',flush=True)\nsys.stdin.buffer.read(1)\n");
                var(r,j,d,i)=Setup();var frame=d.Capture(d.Current,true,CancellationToken.None);var input=new ArchInput(helperPath,start=>{process=System.Diagnostics.Process.Start(ComputerProcess.Start("/usr/bin/python3",new[]{"-u",helperPath}));pid=process.Id;return process;});
                Check(Reject(()=>input.Execute(new ComputerAction{Action="click",X=10,Y=10,Button="left"},frame,d,(x,y)=>{if(++guards==3)throw new InvalidOperationException("revoked before G");},CancellationToken.None,(x,y)=>completions++))&&completions==0,"production pre-delivery guard rejection cannot enter the completion seam");
                Check(pid>0&&!Directory.Exists("/proc/"+pid),"failed production delivery terminates its helper before returning");
            }
            finally{process?.Dispose();File.Delete(helperPath);}
        }
    }
}
