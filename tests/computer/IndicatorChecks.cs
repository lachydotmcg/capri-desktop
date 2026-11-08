using System;using System.Collections.Generic;using System.Threading;using CapriDesktop;
internal static class IndicatorChecks {
 class Desktop:IComputerDesktop {
 public Action DuringCapture; public ComputerSnapshot Current=new ComputerSnapshot("session","layout",new ComputerTarget("0x123",123,"1000:456","fixture",new ComputerRect(0,0,800,600),1,2),new ComputerOutput("DP-1",new ComputerRect(0,0,1280,960),1,0,1280,960),100,100,true);
 public ComputerSnapshot Snapshot(string output=null,bool focus=false)=>Current;
 public ComputerFrame Capture(ComputerSnapshot source,bool crop,CancellationToken token){DuringCapture?.Invoke();return new ComputerFrame{Source=Current,Area=Current.Target.Area,Width=800,Height=600,Jpeg=new byte[]{1}};}
 public void Move(int x,int y,CancellationToken t)=>throw new Exception("unexpected real input seam"); }
 internal static void Run(){ var oldHidden=Chores.Hidden;var oldSign=Chores.SignHidden; int checks=0;try { foreach(var mode in new[]{"throw","cancel","success","signthrow","restorethrow","hidethrow"}) {
 var events=new List<string>();var desktop=new Desktop();bool thought=false;
 Chores.Hidden=hide=>{events.Add(hide?"pointer-hide":"pointer-show");if(hide&&mode=="hidethrow")throw new InvalidOperationException("hide failure");if(!hide&&mode=="restorethrow")throw new InvalidOperationException("restore failure");};
 Chores.SignHidden=hide=>{events.Add(hide?"sign-hide":"sign-show");if(hide&&mode=="signthrow")throw new InvalidOperationException("sign failure");return hide;};
 desktop.DuringCapture=()=>{events.Add("capture");if(mode=="throw")throw new InvalidOperationException("capture failure");if(mode=="cancel")throw new OperationCanceledException();};
 var runner=new ComputerRunner{Desktop=desktop,Account=()=>"public",Think=(j,s,i)=>{thought=true;throw new InvalidOperationException("think intercepted");}};
 try{runner.Run(new ComputerJob{Task="public fixture",Asked="public fixture"},CancellationToken.None);}catch(InvalidOperationException){}catch(OperationCanceledException){}
 string sequence=string.Join(",",events);Console.WriteLine(mode+": "+sequence);
 string expected=mode=="signthrow"?"pointer-hide,sign-hide,pointer-show":mode=="hidethrow"?"pointer-hide,pointer-show":"pointer-hide,sign-hide,capture,pointer-show,sign-show";
 if(sequence!=expected||thought!=(mode=="success"))throw new Exception("indicator cleanup mismatch "+mode+": "+sequence);
 Console.WriteLine("PASS indicator "+mode+" cleanup");
 checks++;
 }Console.WriteLine("Indicator checks "+checks); } finally { Chores.Hidden=oldHidden;Chores.SignHidden=oldSign; } }
}
