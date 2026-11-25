using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

sealed class RecheckApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
}
static class Recheck
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;
    static int passes, fails;
    static Type T(string name) => app.GetType("CapriDesktop."+name,true);
    static Type TypeOf(object o) => o as Type ?? o.GetType();
    static object Target(object o) => o is Type ? null : o;
    static FieldInfo Field(Type t,string n) => t.GetField(n,Flags) ?? (t.BaseType==null ? null : Field(t.BaseType,n));
    static void Put(object o,string n,object v) => Field(TypeOf(o),n).SetValue(Target(o),v);
    static object Get(object o,string n) => Field(TypeOf(o),n) is FieldInfo f ? f.GetValue(Target(o)) : TypeOf(o).GetProperty(n,Flags).GetValue(Target(o));
    static object Call(object o,string n,params object[] a) => TypeOf(o).GetMethods(Flags).Single(m=>m.Name==n && m.GetParameters().Length==a.Length).Invoke(Target(o),a);
    static object New(string n,params object[] a) => Activator.CreateInstance(T(n),Flags & ~BindingFlags.Static,null,a,null);
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Check(string label,object expected,object actual) {
        bool ok=Equals(expected,actual); if(ok) passes++; else fails++;
        Console.WriteLine($"{(ok?"PASS":"FAIL")} {label}: {actual}"+(ok?"":$" (expected: {expected})"));
    }
    static string Bool3(object s) => string.Join('/', Get(s,"Lite"),Get(s,"LiteWatch"),Get(s,"Watch"));
    static object Pet() {
        Call(T("NativeFontBootstrap"),"Initialize");
        AppBuilder.Configure<RecheckApp>().UsePlatformDetect().SetupWithoutStarting();
        return New("Pet",new ClassicDesktopStyleApplicationLifetime { ShutdownMode=ShutdownMode.OnExplicitShutdown });
    }
    static void Watch(string state) {
        object pet=Pet(), saved=Get(pet,"Saved");
        // Exercise actual persistence in scratch, while construction stays in smoke mode (no user-profile or network startup).
        Put(pet,"folder",state);
        Window frame=(Window)New("Mainframe",pet); frame.Show(); Dispatcher.UIThread.RunJobs(); Call(frame,"ShowSettings",true);
        var menu=(MenuItem)Get(pet,"watchItem");
        void Lite(bool lite) => Call(frame,"Change","Lite mode",lite?1:0);
        void Direct(bool on) => Call(pet,"SetWatch",on,false);
        string Tool(string name,string action,string level=null) {
            var args=New("ToolArguments"); Put(args,"Name",name); Put(args,"Action",action); Put(args,"Level",level);
            return (string)Call(T("Hands"),"Do","capri_settings",args);
        }
        foreach(bool prior in new[]{false,true}) {
            Direct(prior); Lite(true);
            Check($"prior={prior}: pause",$"True/{prior}/False",Bool3(saved));
            Check($"prior={prior}: menu disabled",false,menu.IsEnabled);
            Call(frame,"Change","Every",4);
            Check($"prior={prior}: positive Every",$"True/{prior}/False",Bool3(saved));
            Check($"prior={prior}: Every interval retained",60,Get(saved,"GlanceGap"));
            Lite(false);
            Check($"prior={prior}: restored",$"False/False/{prior}",Bool3(saved));
            Check($"prior={prior}: menu re-enabled",true,menu.IsEnabled);
        }
        Direct(false); Lite(true);
        string levels=Tool("glance_gap","set","15 s");
        Console.WriteLine("Every tool output: "+levels);
        Check("tool interval does not wake Watch","True/False/False",Bool3(saved));
        Check("tool interval retained",15,Get(saved,"GlanceGap"));
        Lite(false); Check("tool interval preserves prior off","False/False/False",Bool3(saved));
        Direct(false); Lite(true); Direct(true);
        Check("explicit Watch on deferred, still paused","True/True/False",Bool3(saved));
        Lite(false); Check("explicit Watch on versus exact prior off","False/False/False",Bool3(saved));
        Direct(false); Lite(true);
        string path=Path.Combine(state,"cross.json"), shadow=Path.Combine(state,"cross-new.json");
        // An invalid action is rejected; compare both persisted files. Explicit enable is a different, deferred operation.
        Put(T("Program"),"Smoke",null);
        try {
            Check("isolated snapshot saved",true,Call(pet,"WriteSettings"));
            string hash=Hash(path)+Hash(shadow);
            Check("invalid settings tool action","Choose list, on, off or set.",Tool("watch_my_screen","invalid"));
            Check("invalid action originals SHA-256",hash,Hash(path)+Hash(shadow));
        } finally { Put(T("Program"),"Smoke",state); }
        string reply=Tool("watch_my_screen","on");
        Check("Watch tool exact reply","Couldn't turn Watch my screen on: it's still off. Watch my screen stays paused until Lite mode goes off.",reply);
        Check("Watch tool queues intent without capture","True/True/False",Bool3(saved));
        Lite(false); Check("Watch tool versus exact prior off","False/False/False",Bool3(saved));
        // If enable is deliberately deferred, a later explicit off must still cancel it.
        Direct(false); Lite(true);
        string deferred=Tool("watch_my_screen","on"), cancel=Tool("watch_my_screen","off");
        Console.WriteLine("Tool on then off: "+deferred+" | "+cancel);
        Check("tool on then off remains paused",false,Get(saved,"Watch"));
        Lite(false); Check("tool on then off restores prior off", "False/False/False",Bool3(saved));
        Call(frame,"Shut");
    }
    const string Busy="Lite mode is on, but my brain on your PC is still busy, so it's using your PC's power.";
    const string Advice="Turn Lite mode off and on once it's free to stop it.";
    static object Engine(object pet,string dir) {
        object e=New("Engine",dir), b=Get(pet,"Brain");
        Put(pet,"ownEngine",e); Put(b,"Local",e); Put(b,"LocalPick",((Array)Get(T("Engine"),"Picks")).GetValue(0));
        return e;
    }
    static void WatchContract(string state)
    {
        object pet = Pet();
        const string onReply = "Couldn't turn Watch my screen on: it's still off. Watch my screen stays paused until Lite mode goes off.";
        const string offReply = "Watch my screen was already off.";

        void Use(string dir, object saved)
        {
            Directory.CreateDirectory(dir);
            Put(pet, "folder", dir);
            Put(pet, "Saved", saved);
            Put(Get(pet, "Brain"), "Lite", Get(saved, "Lite"));
        }

        string Tool(string action, bool agent)
        {
            object args = New("ToolArguments");
            Put(args, "Name", "watch_my_screen");
            Put(args, "Action", action);
            Put(args, "FromAgent", agent);
            return (string)Call(T("Hands"), "Do", "capri_settings", args);
        }

        string[] Files(string dir) => new[] { Path.Combine(dir, "cross.json"), Path.Combine(dir, "cross-new.json") };
        string Stored(string dir, string key) => string.Join("/", Files(dir).Select(path =>
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
            return json.RootElement.GetProperty(key).GetBoolean();
        }));

        Put(T("Program"), "Smoke", null);
        try
        {
            foreach (bool prior in new[] { false, true })
            {
                string dir = Path.Combine(state, "prior-" + prior);
                object saved = New("Settings");
                Use(dir, saved);
                Call(pet, "SetWatch", prior, false);
                Call(pet, "SetLite", true);
                Check($"prior={prior}: ON reply", onReply, Tool("on", prior));
                Check($"prior={prior}: deferred on disk", "True/True", Stored(dir, "LiteWatch"));
                foreach (string action in new[] { "", "invalid" })
                {
                    string before = string.Join("/", Files(dir).Select(Hash));
                    Check($"prior={prior}: rejected action '{action}'", "Choose list, on, off or set.", Tool(action, prior));
                    Check($"prior={prior}: rejected action '{action}' preserves both files", before, string.Join("/", Files(dir).Select(Hash)));
                }
                Check($"prior={prior}: OFF reply", offReply, Tool("off", prior));
                Check($"prior={prior}: OFF clears intent immediately", false, Get(saved, "LiteWatch"));
                Check($"prior={prior}: OFF clears intent on disk", "False/False", Stored(dir, "LiteWatch"));
                Check($"prior={prior}: duplicate OFF reply", offReply, Tool("off", prior));
                Check($"prior={prior}: Watch still paused", false, Get(saved, "Watch"));
                Call(pet, "SetLite", false);
                Check($"prior={prior}: latest OFF ask wins", false, Get(saved, "Watch"));
            }

            string legacy = Path.Combine(state, "legacy");
            Use(legacy, New("Settings"));
            Call(pet, "SetWatch", true, false);
            Call(pet, "SetLite", true);
            Check("prior ON saved in both contracts", "True/True", Stored(legacy, "LiteWatchWas"));
            foreach (string path in Files(legacy))
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)).AsObject();
                json.Remove("LiteWatchWas");
                File.WriteAllText(path, json.ToJsonString());
            }
            object[] read = { Files(legacy)[0], null };
            object resumed = Call(T("Pet"), "ReadSettings", read);
            Check("legacy profile read without error", null, read[1]);
            Use(legacy, resumed);
            Call(pet, "SetLite", false);
            Check("legacy profile with prior ON and no later ask restores ON", true, Get(resumed, "Watch"));
        }
        finally { Put(T("Program"), "Smoke", state); }
    }
    static void Retry(string dir) {
        object pet=Pet(); double t=1000;
        Put(pet,"MoveClock",(Func<double>)(()=>t));
        var requested=new List<double>(); var queue=new Queue<Action>();
        Put(pet,"LiteRestQueue",(Action<Action>)(work=>{ requested.Add(t); queue.Enqueue(work); }));
        int tried=0;
        Put(pet,"LiteRestTry",(Func<bool>)(()=>{ tried++; return false; }));
        void Tick(double at) { t=at; Call(pet,"CheckLiteRest",at); }
        void Drain() { while(queue.TryDequeue(out var work)) work(); }
        Call(pet,"SetLite",true);
        Tick(999.999); Check("nothing scheduled before enable time",0,queue.Count);
        for(int i=0;i!=80;i++) { Tick(1000+i); Tick(1000+i); Drain(); }
        Check("12 asks at five-second boundaries","1000,1005,1010,1015,1020,1025,1030,1035,1040,1045,1050,1055",string.Join(',',requested));
        Check("12 worker calls",12,tried);
        object signs=Call(pet,"ReadSlowSigns"); Check("live flags after bound",false,Get(signs,"Winding"));
        Put(signs,"Kind","built-in"); Put(signs,"Running",true); Put(signs,"Threads",16); Put(signs,"RamFree",-1L); Put(signs,"Busy",-1d);
        Check("give-up exact diagnostic","From a check of your PC just now: "+Busy+" What helps: "+Advice+" Lite mode is on.",Call(T("Pet"),"SlowFound",signs));
        Call(pet,"SetLite",false); Tick(1100); Check("no job queued while off",0,queue.Count);
        requested.Clear(); tried=0; t=2000;
        Put(pet,"LiteRestTry",(Func<bool>)(()=>++tried==3)); Call(pet,"SetLite",true);
        Tick(2000); Tick(2003); Check("no concurrent queued attempts",1,queue.Count); Drain();
        foreach(double at in new[]{2004.999,2005,2009.999,2010,2015,2080}) { Tick(at); Drain(); }
        Check("stops retrying once free","2000,2005,2010",string.Join(',',requested)); Check("one success then no more calls",3,tried);
        Call(pet,"SetLite",false);
        Put(pet,"LiteRestTry",null);
        object e=Engine(pet,dir);
        // Cover the real scheduler's engine-state gate with a harmless process handle, not an inference model.
        void ModelWithProcess(bool started) {
            using var child=Process.Start("/usr/bin/sleep","15");
            try {
                requested.Clear(); t=started?3000:4000;
                if(started) { Put(e,"server",Process.GetProcessById(child.Id)); Call(e,"Begin"); }
                else Put(e,"loading",1);
                Call(pet,"SetLite",true); Tick(t); Drain();
                Check("active inference/loading remains running before free",false,child.HasExited);
                if(started) Call(e,"End");
                else { Put(e,"server",Process.GetProcessById(child.Id)); Put(e,"loading",0); }
                Tick(t+5); Drain(); Tick(t+5); Drain(); Tick(t+5); Drain();
                Check(started?"real Engine.Rest stops after busy clears":"real Engine.Rest stops after load starts late",true,child.WaitForExit(600));
                Check(started?"busy scheduling state":"loading scheduling state",started?"3000,3005":"4005",string.Join(',',requested));
            } finally {
                Call(pet,"SetLite",false); Call(e,"Stop"); if(!child.HasExited) child.Kill(); child.WaitForExit();
            }
        }
        ModelWithProcess(true); ModelWithProcess(false);
    }
    static void PriorRetry(string dir) {
        object pet=Pet(), e=Engine(pet,dir);
        foreach(bool started in new[]{true,false}) {
            using var child=Process.Start("/usr/bin/sleep","15");
            try {
                if(started) { Put(e,"server",Process.GetProcessById(child.Id)); Call(e,"Begin"); }
                else Put(e,"loading",1);
                Call(pet,"SetLite",true);
                Check("prior rest worker drained",true,SpinWait.SpinUntil(()=>Equals(Get(pet,"liteResting"),false),2500));
                if(started) Call(e,"End");
                else { Put(e,"server",Process.GetProcessById(child.Id)); Put(e,"loading",0); }
                Check(started?"prior stop after busy clears":"prior stop after load starts late",true,child.WaitForExit(600));
            } finally {
                Call(pet,"SetLite",false); Call(e,"Stop"); if(!child.HasExited) child.Kill(); child.WaitForExit();
            }
        }
    }
    [STAThread] static int Main(string[] args) {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        var source=Path.GetFullPath(args[1]); var r=new AssemblyDependencyResolver(source);
        AssemblyLoadContext.Default.Resolving+=(context,name)=>r.ResolveAssemblyToPath(name) is string p ? context.LoadFromAssemblyPath(p) : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll+=(a,n)=>r.ResolveUnmanagedDllToPath(n) is string p ? System.Runtime.InteropServices.NativeLibrary.Load(p) : 0;
        app=AssemblyLoadContext.Default.LoadFromAssemblyPath(source); var state=Path.GetFullPath(args[2]); Directory.CreateDirectory(state); Put(T("Program"),"Smoke",state);
        if(args[0]=="watch") Watch(state); else if(args[0]=="watch-contract") WatchContract(state); else if(args[0]=="retry-base") PriorRetry(state); else Retry(state);
        Console.WriteLine($"passes={passes} failures={fails}"); return fails==0?0:1;
    }
}
