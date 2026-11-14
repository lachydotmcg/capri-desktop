using System; using System.IO; using System.Runtime.Serialization;
namespace CapriDesktop {
static class Hands { public const string CrashKind="fake crash screen"; public static bool Agrees(string text)=>false; public static string Asked(string text)=>text; public static bool Failed(string result)=>false; public static void WebsiteArgs(ToolArguments args){} }
static class Brain { public static string SystemLanguage(System.Globalization.CultureInfo c)=>"en"; public static void LogAt(string p,string tag,int n,string msg)=>File.AppendAllText(p,msg+"\n"); }
[DataContract] class Settings { [DataMember] public string Language; [DataMember]public int X,Y; [DataMember]public bool Placed; }
[DataContract] class NewSettings {}
partial class Pet { private bool settingsWriteBlocked; private string unreadableNotice; public void LoadFixture(){Saved=Load();} public bool Blocked=>settingsWriteBlocked; public string Notice=>unreadableNotice; public Settings Saved; public string folder; private (int X,int Y) anchor; void NotifySettings(){} static Settings Kept(Settings s,NewSettings n)=>s; static NewSettings Newest(Settings s)=>new(); sealed class BodyStub { public double Size; } private BodyStub body = new BodyStub(); private double SitAcross => SitX; internal const double SitX = 0.30; private int Px(double dip)=>(int)System.Math.Round(dip); }
}
