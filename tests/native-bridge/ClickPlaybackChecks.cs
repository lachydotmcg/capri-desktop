using System;
using CapriDesktop;
internal static class ClickPlaybackChecks
{
    internal static void Run()
    {
        int checks=0; void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        var clock=new ClickPlayback();Check(clock.Frame(0)==-1,"no click exists before delivery");clock.Begin(10);
        Check(clock.Frame(10)==0,"delivered click begins with cell0");Check(clock.Frame(10.1)==5 || clock.Frame(10.1)==6,"elapsed time selects the strip frame despite a missed tick");
        Check(clock.Frame(10+19.5/60)==19,"last authored frame remains visible before deadline");Check(clock.Frame(10+ClickPlayback.Duration+0.00001)==-1,"effect completes after20frames at60fps");
        Check(clock.Frame(9)==-1 && clock.Frame(double.PositiveInfinity)==-1,"invalid or earlier clock cannot wrap a frame");clock.Begin(20);Check(clock.Frame(20.25)==15,"a new click replaces the previous timeline");clock.Clear();Check(clock.Frame(20.26)==-1,"hide or cancel clears click immediately");
        Check(ClickPlayback.Cell==144 && ClickPlayback.Width==72,"released cell is half size in100percent display pixels");
        Console.WriteLine("PASS click playback checks="+checks);
    }
}
