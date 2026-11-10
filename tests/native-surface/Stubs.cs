using System.Text.Json;
using System.Collections.Concurrent;
namespace Avalonia {
 public record struct PixelPoint(int X,int Y);
 public record struct PixelSize(int Width,int Height);
 public record struct Size(double Width,double Height);
 public record struct Point(double X,double Y);
 public record struct PixelRect(int X,int Y,int Width,int Height);
 public class Visual { public static object IsVisibleProperty = new(); }
 public class Change { public object Property; }
}
namespace Avalonia.Threading { public class Dispatcher { public static Dispatcher UIThread = new(); public void Post(Action a)=>a(); } }
namespace Avalonia.Controls.Platform { }
namespace Avalonia.Controls {
 public enum X11NetWmWindowType { Utility }
 public static class X11Properties { public static void SetWmClass(Window w,string c) { w.NativeClass=c; } public static void SetNetWmWindowType(Window w,X11NetWmWindowType t) {} }
 public class Screen { public string DisplayName="DP-1"; public Avalonia.PixelRect Bounds=new(0,0,1920,1080); }
 public class Screens { public List<Screen> All=new(){new()}; public Screen ScreenFromPoint(Avalonia.PixelPoint p)=>All[0]; }
 public class PlatformHandle { public IntPtr HandleValue=(IntPtr)42; public IntPtr Handle=>HandleValue; }
 public class Window: Avalonia.Visual {
  public bool IsVisible; public string Title="fixture",NativeClass; public Screens Screens=new(); public Avalonia.PixelRect Bounds=new(0,0,100,100); public double DesktopScaling=1;
  public event EventHandler<Avalonia.Change> PropertyChanged; public event EventHandler Opened,Closed;
  public TaskCompletionSource Shown=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public PlatformHandle TryGetPlatformHandle()=>new();
  public void Activate() {}
  public virtual void Show(){ IsVisible=true; Shown.TrySetResult(); Opened?.Invoke(this,EventArgs.Empty); }
  public virtual void Hide(){IsVisible=false;PropertyChanged?.Invoke(this,new(){Property=IsVisibleProperty});}
  public void Close(){Hide();Closed?.Invoke(this,EventArgs.Empty);}
 }
}
namespace CapriDesktop {
 public class Bubble: Avalonia.Controls.Window { }
 public class Output { public string Name="DP-1"; public int Workspace=1; public Avalonia.Size Logical=new(1920,1080); }
 public class Snapshot { public bool Locked; public string LayoutId="layout"; public Output[] Outputs={new()}; }
 public class HyprlandDisplayMap { public Output Output; public HyprlandDisplayMap(Output o,Avalonia.PixelRect b){Output=o;} public Avalonia.Point ToLogical(Avalonia.PixelPoint p)=>new(p.X,p.Y); }
 public class HyprlandDesktop {
  public List<string> Requests=new(); public bool Disposed;public int DisableCount;public void Dispose() {Disposed=true;}
  public static HyprlandDesktop Current; public Snapshot VisualSnapshot=new(); public long SurfaceRevision; public string Class; public bool LockAtFinalProperty, FailApply; public int Moves;
  public TaskCompletionSource MoveEntered=new(TaskCreationOptions.RunContinuationsAsynchronously), MoveRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource RuleEntered=new(TaskCreationOptions.RunContinuationsAsynchronously), RuleRelease=new(TaskCreationOptions.RunContinuationsAsynchronously); public bool HoldCleanup; public TaskCompletionSource CleanupEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),CleanupRelease=new(TaskCreationOptions.RunContinuationsAsynchronously); public bool HoldMove,HoldRule,HoldApply; public TaskCompletionSource ApplyEntered=new(TaskCreationOptions.RunContinuationsAsynchronously), ApplyRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task<string> QueryAsync(string q,CancellationToken c)=>Task.FromResult(JsonSerializer.Serialize(new[]{new{pid=Environment.ProcessId,@class=Class,title="fixture",mapped=true,address="0x1"}}));
  public async Task<string> SurfaceRequest(string q,CancellationToken c){ Requests.Add(q);
   if(q.Contains("rules.passive:set_enabled(false)")){DisableCount++;return "ok";} if(q.Contains("hl.window_rule")){RuleEntered.TrySetResult();if(HoldRule)await RuleRelease.Task.WaitAsync(c).ConfigureAwait(false);return "ok";}
   if(q.StartsWith("/getprop")){var k=q.Split(' ')[^1];if(k=="stay_focused" && HoldApply){ApplyEntered.TrySetResult();await ApplyRelease.Task.WaitAsync(c).ConfigureAwait(false);} if(k=="no_focus" && Class?.Contains(".Decoration.")==true)return "true";if(k=="no_follow_mouse" && Class?.StartsWith("Capri.Passive.")==true)return "true";if(k=="stay_focused"&&LockAtFinalProperty)VisualSnapshot.Locked=true;return k switch {"border_size" or "rounding"=>"0","decorate" or "no_focus" or "no_follow_mouse" or "stay_focused"=>"false",_=>"true"};}
   if(q.Contains("relative=false")){MoveEntered.TrySetResult();if(HoldMove)await MoveRelease.Task.WaitAsync(c).ConfigureAwait(false);Moves++;return "ok";}
   if(HoldCleanup && q.Contains("if w then apply")){CleanupEntered.TrySetResult();await CleanupRelease.Task.WaitAsync(c).ConfigureAwait(false);return "ok";} if(FailApply)throw new InvalidOperationException("fixture apply failure");return "ok";
  }
 }
 public static class HyprlandJson { public static int Int(JsonElement r,string k)=>r.GetProperty(k).GetInt32();public static string String(JsonElement r,string k)=>r.GetProperty(k).GetString();public static bool Bool(JsonElement r,string k)=>r.GetProperty(k).GetBoolean();public static long Address(string a)=>Convert.ToInt64(a[2..],16); }
 public static class LinuxProcess { public static string Identity(int p)=>"fixture-process"; }
 public static class NativeX11 { public static void Configure(Avalonia.Controls.Window w,bool p){} }
 public static class NativeMotionTrace { public static void Emit(string a,long b,long c,double d,double e){} }
}
