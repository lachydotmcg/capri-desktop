using System.Reflection;
using Avalonia.Controls;
namespace CapriDesktop;
internal static class Program {
 sealed class CountContext:SynchronizationContext { public int Posts; public override void Post(SendOrPostCallback d,object s){Interlocked.Increment(ref Posts);ThreadPool.QueueUserWorkItem(_=>d(s));} }
 static int checks; internal static void Crashed(Exception e){if(e.Message != "Capri surface policy: fixture apply failure")throw new InvalidOperationException("Unexpected product error in fixture",e);Console.WriteLine("EXPECTED_FIXTURE_ERROR: "+e.Message);}
 static void Check(bool b,string m){if(!b)throw new Exception("FAIL: "+m);checks++;Console.WriteLine("PASS: "+m);}
 static object Binding(Window w){var table=typeof(NativeSurface).GetField("bindings",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);var a=new object[]{w,null};table.GetType().GetMethod("TryGetValue")!.Invoke(table,a);return a[1];}
 static Task Worker(Window w)=>(Task)typeof(NativeSurface).GetField("worker",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Binding(w));
 static Window Make(HyprlandDesktop d){HyprlandDesktop.Current=d;var w=new Window();NativeSurface.Register(w,NativeSurfaceRole.Mainframe);d.Class=w.NativeClass;return w;}
 static Bubble MakeBubble(HyprlandDesktop d){HyprlandDesktop.Current=d;var w=new Bubble();NativeSurface.Register(w,NativeSurfaceRole.Mainframe);d.Class=w.NativeClass;return w;}
 static string[] Resizes(HyprlandDesktop d)=>d.Requests.Where(q=>q.Contains("hl.dsp.window.resize(")).ToArray();
 static async Task ReplyResizeChecks(){
  // Hold policy application, so many UI updates deterministically reach one worker batch.
  var d=new HyprlandDesktop{HoldApply=true};var w=MakeBubble(d);
  d.VisualSnapshot.Outputs[0].Logical=new(960,540);
  w.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
  for(int height=91;height<=243;height++)NativeSurface.TryMove(w,new(50,60),replySize:new(500,height));
  var applied=NativeSurface.FlushMoves(w);d.ApplyRelease.SetResult();
  Check(await applied.WaitAsync(TimeSpan.FromSeconds(2)),"latest coalesced reply size applies");
  var resize=Resizes(d);Check(resize.Length==1&&d.Moves==1,"rapid reply sizes coalesce into one native transaction");
  string command=resize.Single();
  Check(command.Contains("resize({x=250,y=121.5,relative=false,window=w})"),"reply physical dimensions map through output logical ratio");
  Check(command.Contains("w.pid~="+Environment.ProcessId)&&command.Contains(w.NativeClass)&&command.Contains("w.title~="),"reply resize retains process class and title identity guard");
  Check(command.IndexOf("window.resize(",StringComparison.Ordinal)<command.LastIndexOf("window.move({x=",StringComparison.Ordinal),"reply resize precedes final anchor position");
  NativeSurface.TryMove(w,new(51,61),replySize:new(500,243));
  Check(await NativeSurface.FlushMoves(w).WaitAsync(TimeSpan.FromSeconds(2))&&Resizes(d).Length==1,"position-only update does not repeat unchanged reply resize");
  NativeSurface.TryMove(w,new(51,61),replySize:new(500,91));
  Check(await NativeSurface.FlushMoves(w).WaitAsync(TimeSpan.FromSeconds(2))&&Resizes(d).Length==2&&Resizes(d)[^1].Contains("y=45.5,"),"same-position short reply resizes after long reply");
  w.Hide();await Worker(w).WaitAsync(TimeSpan.FromSeconds(2));
  d.ApplyEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);d.ApplyRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
  w.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));NativeSurface.TryMove(w,new(51,61),replySize:new(500,91));
  applied=NativeSurface.FlushMoves(w);d.ApplyRelease.SetResult();
  Check(await applied.WaitAsync(TimeSpan.FromSeconds(2))&&Resizes(d).Length==3,"remapped reply reapplies geometry instead of trusting old cache");
  w.Hide();await Worker(w).WaitAsync(TimeSpan.FromSeconds(2));

  d=new(){HoldApply=true};var ordinary=Make(d);ordinary.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
  NativeSurface.TryMove(ordinary,new(10,20),replySize:new(500,243));applied=NativeSurface.FlushMoves(ordinary);d.ApplyRelease.SetResult();
  Check(await applied.WaitAsync(TimeSpan.FromSeconds(2))&&d.Moves==1&&Resizes(d).Length==0,"non-bubble surface ignores supplied reply size and still moves");
  ordinary.Hide();await Worker(ordinary).WaitAsync(TimeSpan.FromSeconds(2));

  foreach(string mode in new[]{"stale-layout","locked","hidden","canceled-generation"}){
   d=new(){HoldApply=true};w=MakeBubble(d);w.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
   NativeSurface.TryMove(w,new(50,60),replySize:new(500,243));applied=NativeSurface.FlushMoves(w);
   if(mode=="stale-layout")d.VisualSnapshot.LayoutId="replacement";
   if(mode=="locked")d.LockAtFinalProperty=true;
   if(mode=="hidden")w.Hide();
   if(mode=="canceled-generation")NativeSurface.CancelMoves(w);
   d.ApplyRelease.SetResult();
   try{await applied.WaitAsync(TimeSpan.FromSeconds(2));}catch(OperationCanceledException) when(mode=="hidden"){}
   Check(d.Moves==0&&Resizes(d).Length==0,mode+" excludes pending reply resize from native transport");
   w.Hide();await Worker(w).WaitAsync(TimeSpan.FromSeconds(2));
  }
 }
 static async Task Main(){
  await ReplyResizeChecks();
  var d=new HyprlandDesktop();var w=Make(d);Check(await NativeSurface.FlushMoves(w).WaitAsync(TimeSpan.FromSeconds(2))==false,"no visible worker returns false immediately");
  foreach(var mode in new[]{"locked","stale-layout","hidden","normal"}){
   d=new(){HoldApply=true};w=Make(d);w.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));NativeSurface.TryMove(w,new(50,60));var fence=NativeSurface.FlushMoves(w);Check(!fence.IsCompleted,mode+" release fence waits behind active worker");
   if(mode=="locked")d.LockAtFinalProperty=true;if(mode=="stale-layout")d.VisualSnapshot.LayoutId="new-layout";
   if(mode=="hidden")w.Hide();d.ApplyRelease.SetResult();
   if(mode=="hidden"){bool canceled=false;try{await fence.WaitAsync(TimeSpan.FromSeconds(2));}catch(OperationCanceledException){canceled=true;}Check(canceled,"hide cancels pending release fence");}
   else {var applied=await fence.WaitAsync(TimeSpan.FromSeconds(2));Check(applied==(mode=="normal"),mode+" reports application truth");Check(d.Moves==(mode=="normal"?1:0),mode+" actual fake transport move count");}
   w.Hide();await Worker(w).WaitAsync(TimeSpan.FromSeconds(2));
  }
  d=new(){FailApply=true};w=Make(d);w.Show();await Worker(w).WaitAsync(TimeSpan.FromSeconds(2));Check(await NativeSurface.FlushMoves(w).WaitAsync(TimeSpan.FromSeconds(2))==false,"failed worker returns false instead of orphan fence");w.Hide();
  d=new(){HoldRule=true};HyprlandDesktop.Current=d;var p=new PassiveWindow();NativeSurface.Register(p,NativeSurfaceRole.Passive);d.Class=p.NativeClass;p.Show();await d.RuleEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));p.Hide();p.Show();d.RuleRelease.SetResult();await p.Shown.Task.WaitAsync(TimeSpan.FromSeconds(2));Check(p.IsVisible,"production pending Show-Hide-Show honors latest visible intent");p.Hide();await Worker(p).WaitAsync(TimeSpan.FromSeconds(2));
  d=new(){HoldCleanup=true,FailApply=true};HyprlandDesktop.Current=d;var retiring=new Window();NativeSurface.Register(retiring,NativeSurfaceRole.Composer);d.Class=retiring.NativeClass;retiring.Show();await d.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));var orphan=NativeSurface.FlushMoves(retiring);d.CleanupRelease.SetResult();await Worker(retiring).WaitAsync(TimeSpan.FromSeconds(2));Check(orphan.IsCompletedSuccessfully && !await orphan,"Flush during worker retirement returns false, no orphan");retiring.Hide();
  foreach(bool input in new[]{true,false}) {
   d=new(){HoldApply=true};HyprlandDesktop.Current=d;var decorative=new Window();NativeSurface.Register(decorative,NativeSurfaceRole.Passive,input);d.Class=decorative.NativeClass;
   Check(decorative.NativeClass.Contains(".Decoration.")==!input,"passive class separates decoration from pointer-interactive window "+input);
   decorative.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));NativeSurface.TryMove(decorative,new(10,20));var moved=NativeSurface.FlushMoves(decorative);d.ApplyRelease.SetResult();Check(await moved.WaitAsync(TimeSpan.FromSeconds(2)),"passive role policy validates expected no_focus state "+input);
   Check(d.Requests.Any(q=>q.Contains("no_focus")&&q.Contains("prop=\"no_focus\",value=\""+(input?"false":"true")+"\"")),"generated scoped policy requests correct input state "+input);
   decorative.Hide();await Worker(decorative).WaitAsync(TimeSpan.FromSeconds(2));
  }
  d=new(){HoldApply=true};HyprlandDesktop.Current=d;var editor=new Window();NativeSurface.Register(editor,NativeSurfaceRole.FileEditor);d.Class=editor.NativeClass;
  editor.Show();await d.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));var editorPolicy=NativeSurface.FlushMoves(editor);d.ApplyRelease.SetResult();
  Check(await editorPolicy.WaitAsync(TimeSpan.FromSeconds(2)),"editor accepts verified scoped compositor policy");
  string policy=d.Requests.Single(q=>q.Contains("window.float("));
  Check(policy.Contains("w.pid~="+Environment.ProcessId)&&policy.Contains(editor.NativeClass)&&policy.Contains("w.title~="),"editor retains exact process class and title identity guard");
  Check(!policy.Contains("window.pin(")&&!policy.Contains("stay_focused\",value=\"true"),"editor remains unpinned without composer focus ownership");
  foreach(var value in new[]{("no_blur","true"),("no_shadow","true"),("no_dim","true"),("border_size","0"),("rounding","0"),("decorate","false"),("no_anim","true"),("no_focus","false")})
   Check(policy.Contains("prop=\""+value.Item1+"\",value=\""+value.Item2+"\""),"editor scoped property "+value.Item1+"="+value.Item2);
  editor.Hide();await Worker(editor).WaitAsync(TimeSpan.FromSeconds(2));
  d=new(){HoldRule=true};HyprlandDesktop.Current=d;var ensure=NativePassive.EnsureRule(default);await d.RuleEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));var context=new CountContext();var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(context);var shutting=NativePassive.Shutdown();SynchronizationContext.SetSynchronizationContext(previous);Check(!shutting.IsCompleted&&d.DisableCount==0,"shutdown waits behind admitted rule bootstrap");d.RuleRelease.SetResult();await ensure.WaitAsync(TimeSpan.FromSeconds(2));await shutting.WaitAsync(TimeSpan.FromSeconds(2));Check(d.DisableCount==1&&d.Disposed&&context.Posts==0,"shutdown disables rules then disposes desktop without captured UI continuation");bool denied=false;try{await NativePassive.EnsureRule(default);}catch(OperationCanceledException){denied=true;}Check(denied,"retired lifecycle rejects new rule registration");
  Console.WriteLine("CHECKS="+checks);
 }
}
