using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CapriDesktop;

// A public-only native renderer/controller. No Pet, provider, voice or real profile is opened.
internal static class NativeEditorFixture
{
    internal static string DirectoryPath;
    internal static int Run(string folder)
    {
        DirectoryPath = Path.GetFullPath(folder);
        if (!Path.GetFileName(DirectoryPath).StartsWith("public-editor-",StringComparison.Ordinal)) throw new Exception("Use a new public-editor-* fixture folder.");
        if(Directory.Exists(DirectoryPath)&&Directory.GetFileSystemEntries(DirectoryPath).Length>0)throw new Exception("Use an empty fixture folder.");
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath,"personality.md"),"# Public personality\nYou are a disposable editor fixture.\n");
        File.WriteAllText(Path.Combine(DirectoryPath,"me.md"),"# Public notes\nNo owner data is present.\n");
        try
        {
            NativePassive.Initialize().GetAwaiter().GetResult();
            return AppBuilder.Configure<FixtureApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(Array.Empty<string>(),ShutdownMode.OnExplicitShutdown);
        }
        finally { NativePassive.Shutdown().GetAwaiter().GetResult(); }
    }
    public sealed class FixtureApp:Application
    {
        private NativeFileEditor editor;
        private IClassicDesktopStyleApplicationLifetime desktop;
        private long previous;
        private string asyncProbe, focusProbe;
        private readonly DispatcherTimer poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
        public override void Initialize(){Styles.Add(new FluentTheme());}
        public override void OnFrameworkInitializationCompleted()
        {
            desktop=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime;Open(0);desktop.MainWindow=editor;poll.Tick+=(_,_)=>Poll();poll.Start();base.OnFrameworkInitializationCompleted();
        }
        private void Open(int which)
        {
            if(editor==null||editor.Over)editor=new NativeFileEditor(new[]{Doc("personality.md"),Doc("me.md")});
            editor.Open(which);
        }
        private static NativeEditorDocument Doc(string name)=>new NativeEditorDocument(name,()=>new WrittenNote(Path.Combine(DirectoryPath,name),"",new object()));
        private async Task ProbeAsync()
        {
            await editor.WhenIdle;
            if (editor.Dirty) throw new Exception("Use an unchanged public fixture for the async probe.");
            editor.Motion=false;editor.Ask();
            using var release=new ManualResetEventSlim();
            int uiThread=Environment.CurrentManagedThreadId,readThread=uiThread;
            bool readTimedOut=false;
            NativeEditorDocument SlowDoc(string name)=>new NativeEditorDocument(name,()=>{
                readThread=Environment.CurrentManagedThreadId;
                if(name=="personality.md")readTimedOut=!release.Wait(TimeSpan.FromSeconds(2));
                return new WrittenNote(Path.Combine(DirectoryPath,name),"",new object());
            });
            editor=new NativeFileEditor(new[]{SlowDoc("personality.md"),SlowDoc("me.md")}){Motion=false};
            editor.Open(0);
            Task<bool> queuedSave=editor.Save();
            editor.Turn(1);
            // A callback resumes on the dispatcher while an actual document read is
            // blocked. Synchronous UI I/O times out first and fails these checks.
            await Task.Delay(75);
            bool heartbeat=editor.Busy&&!editor.TextArea.IsEnabled&&!queuedSave.IsCompleted&&!readTimedOut;
            bool rejected=false;try{editor.Draft="must not race the read";}catch(InvalidOperationException){rejected=true;}
            release.Set();
            bool saved=await queuedSave;await editor.WhenIdle;
            if(!heartbeat||!rejected||readThread==uiThread||!saved||editor.FileShown!="me.md"||editor.Busy)
                throw new Exception("Async editor lane failed UI heartbeat, exclusive document ownership or queued operation ordering.");
            asyncProbe="PASS worker read keeps UI responsive; edit rejected while busy; save and tab serialized";
        }
        private async Task ProbeFocusAsync()
        {
            await editor.WhenIdle;
            editor.Activate();
            await Task.Delay(100);
            await editor.WhenIdle;
            if (!editor.IsActive) throw new Exception("Focus the public editor before running focus-probe.");
            editor.FinishOpen(); editor.TextArea.Focus();
            var text = editor.TextArea;
            int length = text.Text?.Length ?? 0;
            text.CaretIndex = Math.Min(8, length); text.SelectionStart = Math.Min(2, length); text.SelectionEnd = Math.Min(7, length);
            int caret = text.CaretIndex, start = text.SelectionStart, end = text.SelectionEnd;
            string before = editor.Draft;
            bool Matches() => editor.IsActive && text.IsKeyboardFocusWithin && text.CaretIndex == caret && text.SelectionStart == start && text.SelectionEnd == end && editor.Draft == before;
            if (!await editor.Save() || !Matches()) throw new Exception("Save lost editor focus, caret or selection.");
            editor.Turn(editor.FileShown == "personality.md" ? 0 : 1);
            await editor.WhenIdle;
            if (!Matches()) throw new Exception("An unchanged same-tab read reset editor focus, caret or selection.");
            focusProbe = "PASS Save and unchanged same-tab read preserve text focus, caret and selection";
        }
        private bool polling;
        private async void Poll()
        {
            if(polling)return;
            string request=Path.Combine(DirectoryPath,"request.json");if(!File.Exists(request))return;
            polling=true;
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(request));var r=doc.RootElement;long id=r.GetProperty("id").GetInt64();if(id==previous)return;previous=id;string error=null;
                try
                {
                    string command=r.GetProperty("command").GetString();
                    switch(command)
                    {
                        case "state":break;
                        case "async-probe":await ProbeAsync();break;
                        case "focus-probe":await ProbeFocusAsync();break;
                        case "open":Open(r.TryGetProperty("which",out var which)?which.GetInt32():0);break;
                        case "draft":editor.Draft=r.GetProperty("text").GetString();break;
                        case "save":await editor.Save();break;
                        case "reload":await editor.Reload();break;
                        case "close":editor.Ask();break;
                        case "guard-save":await editor.GuardSave();break;
                        case "guard-discard":await editor.GuardDiscard();break;
                        case "guard-keep":editor.GuardKeep();break;
                        case "finish":editor.FinishOpen();break;
                        case "external":File.WriteAllText(Path.Combine(DirectoryPath,r.GetProperty("which").GetInt32()==0?"personality.md":"me.md"),r.GetProperty("text").GetString());break;
                        case "shutdown":if(await editor.PrepareShutdownAsync()==null){poll.Stop();desktop.Shutdown();}break;
                        default:throw new Exception("Unknown fixture command.");
                    }
                }
                catch(Exception failure){error=failure.GetType().Name+": "+failure.Message;}
                var box=editor.PageBox;var response=new{id,error,async_probe=asyncProbe,focus_probe=focusProbe,pid=Environment.ProcessId,window_id=editor.TryGetPlatformHandle()?.Handle.ToInt64(),editor.Over,editor.Busy,editor.Dirty,editor.Guarding,editor.Resting,editor.Frame,editor.FileShown,editor.Draft,editor.Status,editor.BlurStatus,transparency=editor.EffectiveTransparency.ToString(),x=editor.Position.X,y=editor.Position.Y,width=editor.Width,height=editor.Height,scale=editor.RenderScaling,page=new{box.X,box.Y,box.Width,box.Height},caret=editor.TextArea.CaretIndex,selection_start=editor.TextArea.SelectionStart,selection_end=editor.TextArea.SelectionEnd,keyboard_focus=editor.TextArea.IsKeyboardFocusWithin};
                string temporary=Path.Combine(DirectoryPath,"response.tmp");File.WriteAllText(temporary,JsonSerializer.Serialize(response,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,Path.Combine(DirectoryPath,"response.json"),true);
            }
            catch(JsonException){}catch(IOException){}finally{polling=false;}
        }
    }
}
