using System; using System.IO; using CapriDesktop;
class Program {
 public static object Smoke=null; static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Main(){
 Check(Security.Count==15&&Security.ModTools==13&&Security.Terminal==14&&Security.Names.Length==15&&Security.Lines.Length==15,"15 cross security toggles, mod tools at 13 and terminal at 14");
 foreach(var pair in new[]{(Security.Lockdown, true),(Security.Guarded,true),(Security.Trusted,true),(Security.Blackwall,false)}) { Security.Pick(pair.Item1);Check(Security.On(Security.Personal)==pair.Item2&&Security.On(Security.ModTools)==(pair.Item1>=Security.Trusted)&&Security.On(Security.Terminal)==(pair.Item1==Security.Blackwall),"preset personal, mod tools and terminal defaults "+pair.Item1); }
 foreach(var s in new[]{"0000000000","1111111000","1000000000"}) {Security.Load(s);Check(Security.On(Security.Personal),"legacy privacy on "+s);}
 Security.Load("1111111111");Check(!Security.On(Security.Personal),"legacy blackwall privacy off");
 Security.Load("111111111101");Check(Security.On(Security.Personal),"explicit blackwall privacy override on");
 Security.Load("111111100010",true);Check(Security.On(Security.Friends)&&Security.On(Security.Wake)&&!Security.On(Security.Personal),"friends wake personal independent");
 Security.Jobs(true);Check(Security.On(Security.Friends)&&Security.On(Security.Wake)&&!Security.On(Security.Personal),"Jobs preserves independent toggles");
 for(int bits=0;bits<(1<<Security.Count);bits++){
 for(int i=0;i<Security.Count;i++)Security.Set(i,(bits&(1<<i))!=0);
 var saved=Security.Saved;
 bool terminal=(bits&(1<<Security.Terminal))!=0, legacyTerminal=(bits&0x3FF)==0x3FF;
 if(terminal!=legacyTerminal&&(saved.Length!=15||saved[12]!=((bits&(1<<Security.ModTools))!=0?'1':'0')||saved[13]!='0'||saved[14]!=(terminal?'1':'0')))throw new Exception("terminal save layout "+bits);
 if(terminal==legacyTerminal&&saved.Length>13)throw new Exception("unnecessary terminal extension "+bits);
 Security.Load(saved,(bits&(1<<Security.Wake))!=0);if(Security.Mask!=bits)throw new Exception("roundtrip "+bits);
 int independent=bits&~0x3FF;
 Security.Jobs(true);if(Security.Mask!=(independent|0x3FF))throw new Exception("Blackwall jobs lost toggles "+bits);
 Security.Jobs(false);if(Security.Mask!=(independent|0x7F))throw new Exception("Trusted jobs lost toggles "+bits);
 }
 for(int length=10;length<=14;length++)for(int number=0;number<(1<<length);number++){
 char[] marks=new char[length];for(int i=0;i<length;i++)marks[i]=(number&(1<<i))!=0?'1':'0';
 string saved=new string(marks);int ten=number&0x3FF, expected=ten;
 if(length>10&&(number&(1<<10))!=0)expected|=1<<Security.Friends;
 if(length>11?(number&(1<<11))!=0:ten!=0x3FF)expected|=1<<Security.Personal;
 if(length>12?(number&(1<<12))!=0:ten!=0&&ten!=7)expected|=1<<Security.ModTools;
 if(ten==0x3FF)expected|=1<<Security.Terminal;
 for(int wake=0;wake<2;wake++){Security.Load(saved,wake!=0);if(Security.Mask!=(expected|(wake<<Security.Wake)))throw new Exception("legacy "+length+" marks "+saved+" wake "+wake);}
 }
 Check(true,"32768 security mask roundtrips, independent job toggles and all 10-14 mark legacy saves");
 foreach(var invalid in new[]{null,"","invalid","111111111",new string('1',16),new string('1',14)+"x"}){Security.Load(invalid);if(Security.Mask!=Security.MaskOf(Security.Trusted))throw new Exception("invalid security settings "+invalid);}
 Check(true,"invalid security settings defaults");
 var terminalCommand=new ToolArguments{Command="find /tmp -delete",Cwd="/tmp"};
 foreach(var preset in new[]{Security.Lockdown,Security.Guarded,Security.Trusted}) {
 Security.Pick(preset);Check(!Security.On(Security.Terminal)&&Security.Asks("run_terminal",terminalCommand)&&Security.IsHold(Security.Gate("run_terminal",terminalCommand)),"terminal asks in preset "+preset);
 int ran=0;Security.Resume("yes",(tool,args)=>{Check(Security.Gate(tool,args)==null,"confirmed terminal passes once");ran++;return "ran";});Check(ran==1&&Security.Asks("run_terminal",terminalCommand),"terminal confirmation expires");
 }
 foreach(var legacy in new[]{"0000000000","1110000000","1111111000"}){Security.Load(legacy);Check(!Security.On(Security.Terminal),"legacy terminal asks "+legacy);}
 Security.Load("1111111111");Check(Security.On(Security.Terminal),"legacy Blackwall keeps terminal permission");
 Security.Pick(Security.Trusted);Security.Set(Security.Terminal,true);Security.Load(Security.Saved);Check(Security.On(Security.Terminal)&&Security.On(Security.ModTools)&&Security.Preset==Security.Custom&&!Security.Asks("run_terminal",terminalCommand)&&Security.Gate("run_terminal",terminalCommand)==null,"terminal override persists independently of shortcuts and mod tools");
 Security.Set(Security.ModTools,false);Security.Jobs(false);Check(Security.On(Security.Terminal)&&!Security.On(Security.ModTools),"job presets preserve terminal and mod tools choices");
 Security.Pick(Security.Blackwall);Security.Set(Security.Terminal,false);Security.Load(Security.Saved,true);Check(!Security.On(Security.Terminal)&&Security.Asks("run_terminal",terminalCommand)&&Security.Preset==Security.Custom,"Blackwall terminal override persists");
 Security.Drop();
 string[] env={"XDG_SESSION_ID","XDG_CURRENT_DESKTOP","HYPRLAND_INSTANCE_SIGNATURE"};var old=Array.ConvertAll(env,Environment.GetEnvironmentVariable);
 try {
 Environment.SetEnvironmentVariable(env[1],"KDE");Environment.SetEnvironmentVariable(env[2],null);
 foreach(var invalid in new[]{"","bad/session","bad\nvalue",new string('a',65)}){Environment.SetEnvironmentVariable(env[0],invalid);Check(Limbs.PowerCommands("sign_out",false)==null&&Limbs.PowerCommands("lock",false)==null,"invalid session rejected "+invalid.Length);}
 Environment.SetEnvironmentVariable(env[0],"c2");Check(Limbs.PowerCommands("sign_out",false)[0][0]=="qdbus6","KDE signout scoped command");
 Environment.SetEnvironmentVariable(env[2],"synthetic-hypr-signature");var command=Limbs.PowerCommands("sign_out",false)[0];Check(command[0]=="/usr/bin/busctl"&&command[6]=="TerminateSession"&&command[8]=="c2","Hyprland retains exact validated logind session");
 Check(Limbs.PowerCommands("lock",false)[0][6]=="LockSession","lock exact session method");Check(Limbs.PowerCommands("made_up",false)==null,"unknown power action rejects");
 } finally {for(int i=0;i<env.Length;i++)Environment.SetEnvironmentVariable(env[i],old[i]);}
 string root=Path.Combine(AppContext.BaseDirectory,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root); string path=Path.Combine(root,"cross.json"), newer=Path.Combine(root,"cross-new.json");byte[] bad=System.Text.Encoding.UTF8.GetBytes("{broken-public-fixture-secret-canary"),newbytes=System.Text.Encoding.UTF8.GetBytes("{preserve-second-public-fixture");File.WriteAllBytes(path,bad);File.WriteAllBytes(newer,newbytes);Directory.CreateDirectory(Path.Combine(root,Pet.UnreadableName));
 var pet=new Pet{folder=root};pet.LoadFixture();Check(pet.Blocked&&pet.Notice.Contains("won't be saved"),"exact Load propagates failed-backup write block and truthful notice");pet.Save();pet.Save();Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bad),"failed backup and repeated exact Save preserve cross.json bytes");Check(File.ReadAllBytes(newer).AsSpan().SequenceEqual(newbytes),"failed backup and repeated exact Save preserve cross-new.json bytes");Check(!File.ReadAllText(Path.Combine(root,"errors.txt")).Contains("public-fixture-secret-canary"),"failure log excludes malformed contents");
 Directory.Delete(Path.Combine(root,Pet.UnreadableName));pet.Save();Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bad),"existing process remains blocked after external repair until restart");
 var restarted=new Pet{folder=root};restarted.LoadFixture();Check(!restarted.Blocked&&File.ReadAllBytes(Path.Combine(root,Pet.UnreadableName)).AsSpan().SequenceEqual(bad),"restart after repair backs up exact bytes and unblocks");restarted.Save();Check(!File.ReadAllBytes(path).AsSpan().SequenceEqual(bad)&&File.ReadAllBytes(Path.Combine(root,Pet.UnreadableName)).AsSpan().SequenceEqual(bad),"successful backup permits defaults save while preserving original backup");
 string fresh=Path.Combine(root,"fresh");Directory.CreateDirectory(fresh);var install=new Pet{folder=fresh};install.LoadFixture();Check(!install.Blocked&&install.Notice==null,"missing fresh install settings remain unblocked and quiet");install.Save();Check(File.Exists(Path.Combine(fresh,"cross.json"))&&File.Exists(Path.Combine(fresh,"cross-new.json")),"fresh install persists both settings files");var valid=new Pet{folder=fresh};valid.LoadFixture();Check(!valid.Blocked&&valid.Notice==null,"valid settings reload remains unblocked and quiet");
 Console.WriteLine("CHECKS "+checks); }
}
