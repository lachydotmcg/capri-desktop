using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

static class E7Probe
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const string Ready = "Hearing: her own ears · default mic · ready", Starting = "Hearing: her own ears · default mic · starting", Working = "Hearing: her own ears · default mic · working";
    const string NoWords = "No words came back", Quiet = "Mic is too quiet: raise its input volume", Silent = "No sound reached the mic test", NoEars = "Her ears aren't installed", NormalMiss = "I didn't catch that, choom. Try TALK again.";
    const string RecorderStopped = "The microphone recorder stopped";
    static Assembly app;
    static string state, fake, pidDir;
    static int ok, bad;
    static Type TypeOf(string name) => app.GetType("CapriDesktop." + name, true);
    static Type Target(object obj) => obj as Type ?? obj.GetType();
    static object Obj(object obj) => obj is Type ? null : obj;
    static object Get(object obj, string name) => Target(obj).GetProperty(name, All) is PropertyInfo p ? p.GetValue(Obj(obj)) : Target(obj).GetField(name, All).GetValue(Obj(obj));
    static void Set(object obj, string name, object value) => Target(obj).GetField(name, All).SetValue(Obj(obj), value);
    static object Call(object obj, string name, params object[] args) => Target(obj).GetMethod(name, All).Invoke(Obj(obj), args);
    static object Create(string type, params object[] args) => Activator.CreateInstance(TypeOf(type), All, null, args, null);
    static void Check(string label, bool pass, object got = null)
    { if (pass) ok++; else bad++; Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {label}{(got is null ? "" : ": " + got)}"); }
    static void Eq(string label, object got, object expected) => Check(label, Equals(got, expected), $"{got ?? "<null>"}{(Equals(got, expected) ? "" : $" (expected {expected})")}");
    static void Wait(Func<bool> ready, string what, bool ui = false)
    {
        var timer = Stopwatch.StartNew();
        while (!ready() && timer.ElapsedMilliseconds < 5000) { if (ui) Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!ready()) throw new Exception("timed out: " + what);
    }
    static void Section(string name, Action run) { try { run(); } catch (Exception e) { Check(name, false, e); } }
    static int[] Pids() => Directory.GetFiles(pidDir).Select(Path.GetFileName).Select(x => int.TryParse(x, out int i) ? i : 0).Where(i => i != 0).ToArray();
    static bool Gone(int pid) => !Directory.Exists("/proc/" + pid);
    static void Mode(string name)
    {
        pidDir = Directory.CreateDirectory(Path.Combine(state, "pids-" + name + "-" + Guid.NewGuid().ToString("N"))).FullName;
        Environment.SetEnvironmentVariable("E7_MODE", name);
        Environment.SetEnvironmentVariable("E7_PIDS", pidDir);
    }
    static void MainCore()
    {
        object line = Create("HearingLine");
        void Apply(string name, params object[] args) => Call(line, name, args);
        void Text(string label, string expected, string actions = "", bool installed = true)
        {
            var status = Call(line, "Read", installed);
            Eq(label, Get(status, "Text"), expected);
            Eq(label + " actions", string.Join(" | ", ((IEnumerable)Get(status, "Actions")).Cast<object>().Select(a => Call(TypeOf("HearingStatus"), "Name", a))), actions);
        }
        const string Fixes = "Test mic | Type instead";
        Eq("MicAlive", Get(TypeOf("HearingStatus"), "MicAlive"), .0005);
        Eq("QuietWake", Get(TypeOf("HearingStatus"), "QuietWake"), .006);
        Eq("TestSeconds", Get(TypeOf("HearingStatus"), "TestSeconds"), 10);
        Text("ready", Ready); Text("uninstalled", NoEars, "Open ears folder", false);
        Apply("Listening", true); Text("starting", Starting); Apply("Level", .00825); Text("burst", Working);
        Apply("Listening", false); Apply("Missed"); Text("loud miss after stop", NoWords, Fixes);
        Apply("Listening", true); Apply("Level", .000499); Apply("Listening", false); Text("cancel keeps a prior miss", NoWords, Fixes);
        Apply("Listening", true); Apply("Level", .0005); Apply("Listening", false); Apply("Missed"); Text("next session at MicAlive resets old peak", Quiet, Fixes);
        Apply("Listening", true); Apply("Level", .006); Text("QuietWake clears quiet while working", Working);
        Apply("Heard"); Apply("Listening",false); Text("word clears problem", Ready);
        Apply("Listening", true); Apply("Level", 16.0/32768); Apply("Listening", false); Apply("Missed"); Text("sub-MicAlive TALK miss takes quiet path", Quiet, Fixes);
        Apply("StartTest", 9.25); Text("test starting", Starting, "Stop");
        Eq("before ten seconds", Call(line,"TestDue",19.24999),false); Eq("at ten seconds",Call(line,"TestDue",19.25),true);
        Apply("Level",0.0); Apply("TestDone"); Text("zero test", Silent, Fixes);
        Apply("StartTest", 29.5); Apply("Level", 16.0/32768); Apply("TestDone"); Text("nonzero sub-MicAlive test", Quiet, Fixes);
        Apply("StartTest", 51.0); Apply("Level", .006); Apply("TestDone"); Text("passing test", Ready);
        Apply("Failed", " \nExtract sherpa-onnx into Capri/ears. See setup.\t"); Text("setup classifier", NoEars,"Open ears folder");
        Apply("Failed", "  I couldn't hear your default mic.\nCheck sound."); Text("mic classifier", "Mic failed: I couldn't hear your default mic", "Type instead");
        Apply("Failed", "My offline ears couldn't read that. Check the sherpa-onnx and Parakeet setup."); Text("recognizer classifier", "Her ears failed: My offline ears couldn't read that", Fixes);
        Apply("Failed", " \t \n"); Text("blank malformed failure doesn't overwrite", "Her ears failed: My offline ears couldn't read that", Fixes);
        string help = (string)Call(TypeOf("SelfKnow"),"Matching","Why is my microphone not working?",null);
        Console.WriteLine("help="+help);
        Check("honest help", help != null && help.Contains("default microphone") && help.Contains("up to 10 seconds") && help.Contains("she can't switch it here") && !new[] {"Bluetooth", "mute", "20 seconds", "CABLE"}.Any(part => help.Contains(part,StringComparison.OrdinalIgnoreCase)));
    }
    static void Raw()
    {
        var levels = new ConcurrentQueue<double>();
        string recorder = (string)Call(TypeOf("HearingTap"),"Recorder");
        Eq("Linux recorder resolution", recorder, Path.Combine(fake,"parec"));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Mode("chunk");
        var run = (Task)Call(TypeOf("HearingTap"),"Run", (Action<double>)levels.Enqueue, stop.Token);
        Wait(() => File.Exists(Path.Combine(pidDir,"partial")), "319 samples");
        int pid = Pids().Single();
        Eq("recorder arguments", File.ReadAllText(Path.Combine(pidDir,pid.ToString())), "--raw --format=s16le --rate=16000 --channels=1 --latency-msec=50 --client-name=Capri");
        Eq("no level at 319 samples", levels.Count, 0);
        File.WriteAllText(Path.Combine(pidDir,"continue"), "");
        Wait(() => levels.Count == 3,"three 320-sample frames");
        Eq("normalized RMS sequence", string.Join(',',levels.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))), "0.125,1,0");
        stop.Cancel(); run.GetAwaiter().GetResult(); Wait(() => Gone(pid), "reaping parec " + pid);
        Check("parec PID gone on cancel", Gone(pid), pid);
        Mode("bad"); levels.Clear();
        run = (Task)Call(TypeOf("HearingTap"),"Run", (Action<double>)levels.Enqueue, CancellationToken.None);
        string failure = null;
        try { run.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (IOException error) { failure = error.Message; }
        Eq("malformed capture that exits reports recorder failure", failure, RecorderStopped);
        pid = Pids().Single(); Wait(() => Gone(pid), "reaping malformed parec");
        Eq("one-byte malformed capture emits no levels", levels.Count, 0);
    }
    static void Ui(bool candidate)
    {
        Set(TypeOf("Program"),"Smoke",state);
        Call(TypeOf("NativeFontBootstrap"),"Initialize");
        ((Task)Call(TypeOf("NativePassive"),"Initialize")).GetAwaiter().GetResult();
        AppBuilder.Configure<Application>(() => (Application)Create("CapriApp")).UsePlatformDetect().SetupWithoutStarting();
        object pet = Create("Pet", new ClassicDesktopStyleApplicationLifetime());
        Call(pet,"OpenMainframe");
        var frame = (Window)Get(pet,"mainframe");
        frame.Activate(); Dispatcher.UIThread.RunJobs();
        object talk = Get(pet,"Talk"), pill = Get(talk,"Pill"), saved = Get(pet,"Saved");
        double clock = 90.0;
        Set(pet,"MoveClock",(Func<double>)(() => clock));
        void Talk() => Call(pill,"Press",0);
        bool Listening() => (bool)Get(pet,"Listening");
        string Reply() => (string)Get(talk,"Reply");
        string Line() => (string)Get(frame,"HearingText");
        void Sync() { Dispatcher.UIThread.RunJobs(); Call(frame,"Sync"); frame.UpdateLayout(); }
        void Fix(int button=0) => Call(frame,"PressHearing",button);
        void SetListen(Func<Action,CancellationToken,Task<string>> f) => Set(pet,"Listen",f);
        void Seed(Func<Task<string>> f, string expected)
        {
            SetListen((next, token) => f()); Talk();
            Wait(() => !Listening() && Reply() != null, "TALK result", true); Sync();
            Eq("TALK reply", Reply(), expected);
        }
        void LoudMiss() { Set(pet,"HearingTapRun",(Func<Action<double>,CancellationToken,Task>)((push, _) => { push(.0087); return Task.CompletedTask; })); Seed(() => Task.FromResult(""), NormalMiss); Eq("loud TALK warning",Line(),NoWords); }
        SetListen((next, token) => Task.FromResult(""));
        if (candidate) { Set(pet,"HearingInstalled",(Func<bool>)(() => true)); Sync(); }
        Seed(() => Task.FromResult(""),NormalMiss);
        const string Capture = "I couldn't hear your default mic. Check your sound settings and try TALK again.";
        const string Decode = "My offline ears couldn't read that. Check the sherpa-onnx and Parakeet setup.";
        Seed(() => Task.FromException<string>(new IOException(Capture)), Capture);
        if (candidate) Eq("TALK mic heading",Line(),"Mic failed: I couldn't hear your default mic");
        Seed(() => Task.FromException<string>(new IOException(Decode)), Decode);
        if (candidate) Eq("TALK ears heading",Line(),"Her ears failed: My offline ears couldn't read that");
        Seed(() => Task.FromException<string>(new IOException("Extract sherpa-onnx into Capri/ears. See setup.")), "Extract sherpa-onnx into Capri/ears. See setup.");
        if (candidate) Eq("TALK setup heading",Line(),NoEars);
        var page = (Canvas)Get(frame,"Page");
        Control Head(string name) => page.Children.First(c => c.GetType().Name == "NeonHead" && (string)Get(c,"Text") == name);
        Rect Box(Control c) => new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.Width, c.Height);
        Control switcher = (Control)Call(frame,"Row","Listen for Hey Capri");
        Console.WriteLine("CAPRI baseline-comparison y=" + Canvas.GetTop(Head("CAPRI")) + " voice y=" + Canvas.GetTop(Head("VOICE")));
        if (!candidate) { KeyOrder(frame, switcher, null, null); return; }
        string launched = null;
        Set(TypeOf("Limbs"),"Launch",(Action<string,string[]>)((tool,a) => launched = tool + " " + string.Join(' ', a)));
        Eq("Open ears folder label", Get(frame,"HearingButtons"), "Open ears folder"); Fix();
        Eq("Open ears action route", launched, "xdg-open " + Get(pet,"EarsFolder"));
        Eq("install action never sets audio", Get(talk,"Aloud"), false);
        Call(pet,"HearingReset"); Sync(); LoudMiss();
        var note = (Control)Get(frame,"HearingRow");
        var fixes = ((IEnumerable)Get(frame,"hearingFixes")).Cast<Control>().ToArray();
        KeyOrder(frame,switcher,fixes[0],fixes[1]);
        Check("diagnostic warning isn't the bubble or spoken", Reply()==NormalMiss && !Equals(Get(talk,"Aloud"),true));
        var allProblems = new List<string>();
        var voice = Get(pet, "voice");
        for (int tier=0;tier<3;tier++)
        for (int mode=0;mode<4;mode++)
        foreach (bool key in new[]{false,true})
        {
            Call(frame,"PickBrainTier",tier);
            Call(frame,"PickVoiceTier",mode);
            Set(voice,"Key",key ? "verification-only" : null);
            foreach (int scale in new[]{100,150,200})
            for (int book=0;book<3;book++)
            {
                Set(saved,"AdvancedSettings",book==2); Set(saved,"MainframeSize",scale); Call(frame,"ShowSettings", book!=0); Sync();
                if (Math.Abs((double)Get(frame,"Zoom")-scale/100.0)>.001 || page.Width!=(book==0?380:760)) allProblems.Add($"scale {scale}/{book}/{tier}/{mode}/{key}");
                if (book != 0) { if (note.IsVisible || fixes.Any(c=>c.IsVisible)) allProblems.Add($"off page {scale}/{book}/{tier}/{mode}/{key}"); continue; }
                var name = (Control)Get(frame,"nameListeningNote");
                var head = Head("CAPRI");
                if (!note.IsVisible || fixes.Any(c=>!c.IsVisible) || Box((Control)Get(frame,"voiceNote")).Bottom > Box(note).Top || Box(note).Bottom>Box(fixes[0]).Top || Box(fixes[0]).Right>Box(fixes[1]).Left || Box(fixes[0]).Bottom>Box(switcher).Top || Box(switcher).Bottom>Box(name).Top || Box(name).Bottom>Box(head).Top || Box((Control)Get(frame,"updateNote")).Bottom>page.Height) allProblems.Add($"overlap {scale}/{book}/{tier}/{mode}/{key}");
            }
        }
        Check("216 geometry cases (3 tiers × 4 voice modes × 2 key states × 3 pages × 3 scales)",allProblems.Count==0,string.Join("; ",allProblems));
        Call(frame,"PickBrainTier",0); Call(frame,"PickVoiceTier",0);
        Set(saved,"MainframeSize",100); Call(frame,"ShowSettings",false); Sync();
        Fix(1); Eq("Type instead opens existing TYPE", Get(talk,"Typing"), true); ((Window)Get(talk,"Typer")).Hide();
        var realTap = (Func<Action<double>,CancellationToken,Task>)TypeOf("HearingTap").GetMethod("Run",All).CreateDelegate(typeof(Func<Action<double>,CancellationToken,Task>));
        Set(pet,"HearingTapRun",realTap);
        Mode("zero"); clock = 100.25;
        string prior = Line(); Fix();
        Wait(() => Pids().Length==1 && Line()==Working,"test working on zero PCM", true);
        int pid = Pids().Single();
        Eq("midtest button",Get(frame,"HearingButtons"),"Stop");
        Fix();
        Wait(()=>Gone(pid), "Stop kills actual test tap",true); Sync();
        Eq("Stop kills actual test tap PID " + pid, Gone(pid),true);
        Eq("Stop restores prior diagnostics",Line(),prior);
        Eq("Stop leaves existing bubble unchanged",Reply(),NormalMiss);
        Mode("bad"); clock=200.125; Fix();
        Wait(() => Pids().Length == 1 && Gone(Pids()[0]) && !Equals(Get(pet,"HearingTesting"),true),
            "crashed recorder ends the mic test immediately", true);
        Eq("recorder failure visible in the hearing line", Line(), "Mic failed: " + RecorderStopped);
        LoudMiss();
        Set(pet,"HearingTapRun",realTap);
        Mode("partial"); clock=300.125; Fix();
        Wait(() => File.Exists(Path.Combine(pidDir,"partial")), "live recorder sends a partial frame", true);
        pid = Pids().Single();
        clock+=9.999; Call(pet,"TickHearing"); Sync();
        Eq("partial frame still active before due",Get(pet,"HearingTesting"),true);
        Eq("partial frame never showed working",Line(),Starting);
        clock=310.125; Call(pet,"TickHearing"); Sync();
        Eq("timer fires exactly at ten seconds",Get(pet,"HearingTesting"),false);
        Eq("live recorder with no full frame reaches silent result",Line(),Silent);
        Wait(() => Gone(pid), "test deadline stops the live recorder", true);
        if (Line()!=Silent) throw new Exception("can't seed subsequent TALK/Test interaction");
        TapConflict(pet,frame,talk,realTap);
    }
    static void TapConflict(object pet, Window frame, object talk, Func<Action<double>,CancellationToken,Task> realTap)
    {
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0;
        Set(pet,"Listen", (Func<Action,CancellationToken,Task<string>>)((_,token) => { Interlocked.Increment(ref started); return finish.Task; }));
        object pill = Get(talk,"Pill");
        Mode("zero");
        Call(pill,"Press",0);
        Wait(()=>started==1 && Pids().Length==1,"TALK owns tap",true);
        int old=Pids().Single();
        Call(frame,"PressHearing",0);
        Wait(()=>Pids().Length==2 && Gone(old),"Test mic displaces TALK",true);
        int next=Pids().Single(p=>p!=old);
        finish.SetResult("");
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<400) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Check("cancelled TALK must not kill the new Test mic tap",!Gone(next),$"old pid {old}, test pid {next}, test-pid-alive={!Gone(next)}, active={Get(pet,"HearingTesting")}");
        Call(frame,"PressHearing",0);
        Wait(()=>Gone(next),"test tap cleanup",true);
    }
    static void RealLifecycle(bool candidate)
    {
        Set(TypeOf("Program"), "Smoke", state);
        Call(TypeOf("NativeFontBootstrap"), "Initialize");
        ((Task)Call(TypeOf("NativePassive"), "Initialize")).GetAwaiter().GetResult();
        AppBuilder.Configure<Application>(() => (Application)Create("CapriApp")).UsePlatformDetect().SetupWithoutStarting();
        object pet = Create("Pet", new ClassicDesktopStyleApplicationLifetime());
        object talk = Get(pet,"Talk"), pill = Get(talk,"Pill");
        var model = Directory.CreateDirectory(Path.Combine((string)Get(pet,"EarsFolder"), "any-language"));
        foreach (string name in new[]{"tokens.txt","encoder.onnx","decoder.onnx","joiner.onnx"}) File.WriteAllText(Path.Combine(model.FullName,name),"fixture");
        object ears = Create("Ears", model.Parent.Parent.FullName, (Func<string>)(() => null), (Func<string>)(() => "English"));
        var listen = (Func<Action,CancellationToken,Task<string>>)GetDelegate(ears,"Listen",typeof(Func<Action,CancellationToken,Task<string>>));
        Set(pet,"Listen",listen);
        string config = Path.Combine(model.Parent.Parent.FullName,"cross.json");
        File.WriteAllText(config,"{}\n");
        string Sha() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(config)));
        string before = Sha();
        Console.WriteLine("fixture cross.json SHA-256="+before);
        Call(pet,"OpenMainframe");
        object frame = Get(pet,"mainframe");
        string Line() => (string)Get(frame,"HearingText");
        void Sync() { Dispatcher.UIThread.RunJobs(); Call(frame,"Sync"); }
        bool Listening() => (bool)Get(pet,"Listening");
        void Talk() => Call(pill,"Press",0);
        var tokens = new List<CancellationToken>();
        if (candidate)
        {
            var tap = (Func<Action<double>,CancellationToken,Task>)GetDelegate(TypeOf("HearingTap"),"Run",typeof(Func<Action<double>,CancellationToken,Task>));
            Set(pet,"HearingTapRun", (Func<Action<double>,CancellationToken,Task>)((level,stop) => { tokens.Add(stop); return tap(level,stop); }));
        }
        Mode("whisper"); Talk();
        Wait(() => !Listening() && Get(talk,"Reply") is string, "4s of 16-LSB PCM completed", true);
        Sync();
        Eq("real Ears 16-LSB TALK reply",Get(talk,"Reply"),NormalMiss);
        if (candidate) Eq("real Ears 16-LSB diagnostic",Line(),Quiet);
        Eq("whisper unchanged cross.json SHA-256",Sha(),before);
        Mode("utterance"); Talk();
        Wait(() => !Listening() && Get(talk,"Reply") is string, "empty recognition payload", true);
        Sync();
        Eq("real Ears empty recognizer text TALK reply",Get(talk,"Reply"),NormalMiss);
        if (!candidate) return;
        Eq("real Ears loud diagnostic",Line(),NoWords);
        Mode("zero"); Talk();
        Wait(()=>Pids().Length==2,"two live recorder subprocesses from TALK",true);
        Eq("two live recorder subprocesses from TALK",Pids().Count(pid=>!Gone(pid)),2);
        Sync(); Call(frame,"PressHearing",0);
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < 550) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(4); }
        Eq("TALK-to-Test token still live",tokens.Last().IsCancellationRequested,false);
        Eq("TALK-to-Test displayed as active",Get(pet,"HearingTesting"),true);
        Call(frame,"PressHearing",0); Sync();
        Eq("cancel restores real Ears miss",Line(),NoWords);
        Eq("cancel unchanged cross.json SHA-256",Sha(),before);
        Wait(()=>Pids().All(Gone), "all recorders reaped after Stop",true);
        Check("all owned PIDs reaped after Stop",Pids().All(Gone),string.Join(',',Pids()));
    }
    static Delegate GetDelegate(object obj, string method, Type sig) => obj is Type ? Target(obj).GetMethod(method,All).CreateDelegate(sig) : Target(obj).GetMethod(method,All).CreateDelegate(sig,obj);
    static void KeyOrder(Window frame, Control name, Control test, Control type)
    {
        string Name(Control c) => c == null ? "<none>" : AutomationProperties.GetName(c) ?? "<unnamed>";
        Control Held() => (Control)Get(frame,"HeldNow");
        Call(frame,"Hold",name);
        Call(frame,"Strike",Key.Tab,KeyModifiers.None); Eq("Tab after voice controls",Get(frame,"HeldSection"),"CAPRI");
        Call(frame,"Strike",Key.Tab,KeyModifiers.Shift); Eq("Shift-Tab returns to VOICE",Get(frame,"HeldSection"),"VOICE");
        if (test != null)
        {
            Call(frame,"Hold",name);
            Call(frame,"Strike",Key.Up,KeyModifiers.None); Eq("Up from name listening",Held(),type);
            Call(frame,"Strike",Key.Left,KeyModifiers.None); Check("Left to Test mic",Held()==test,Name(Held()));
            Call(frame,"Strike",Key.Right,KeyModifiers.None); Check("Right to Type instead",Held()==type,Name(Held()));
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        string assembly = Path.GetFullPath(args[0]);
        app = Assembly.LoadFrom(assembly);
        AssemblyLoadContext.Default.Resolving += (_,n) => File.Exists(Path.Combine(Path.GetDirectoryName(assembly),n.Name+".dll")) ? Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(assembly),n.Name+".dll")) : null;
        state = Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName;
        fake = Path.GetFullPath(args[2]);
        string path=Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH",fake+Path.PathSeparator+path);
        try
        {
            if (args[3]=="core") { Section("state-machine and help",MainCore); Section("tap framing and cancellation",Raw); }
            else if (args[3].EndsWith("-lifecycle")) Section(args[3],()=>RealLifecycle(args[3]=="candidate-lifecycle"));
            else Section(args[3]+" UI",()=>Ui(args[3]=="candidate"));
        }
        finally { foreach (string dir in Directory.GetDirectories(state,"pids-*")) foreach (string f in Directory.GetFiles(dir)) if(int.TryParse(Path.GetFileName(f),out int pid) && !Gone(pid)) try { Process.GetProcessById(pid).Kill(); } catch (ArgumentException) { } }
        Console.WriteLine($"TOTAL {ok} passed, {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
