using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using CapriDesktop;

internal static class NameHookChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        Check(!new Settings().ListenCapri, "new native name listening defaults off");
        using var legacy = new MemoryStream(Encoding.UTF8.GetBytes("{\"NativePlan\":true}"));
        var old = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(legacy);
        Check(!old.ListenCapri, "legacy profile without new opt-in does not activate microphone");
        using var saved = new MemoryStream();
        new DataContractJsonSerializer(typeof(Settings)).WriteObject(saved, new Settings { ListenCapri = true });
        saved.Position = 0;
        Check(((Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(saved)).ListenCapri, "explicit native opt-in persists");
        foreach (string text in new[] { "Capri stop", "Capri, stop", "Hey Capri: stop", "stop Capri" })
            Check(NameListeningText.Request(text) == "stop", "named Stop reaches existing actual cancellation: " + text);
        Check(NameListeningText.Request("Capri") == "", "name-only utterance opens chat without empty brain request");
        Check(NameListeningText.Request("Capri, open a site") == "open a site", "leading name is removed from actual request");
        Check(NameListeningText.Request("open a site, Capri") == "open a site", "trailing address is removed from actual request");
        Check(NameListeningText.Request("tell me about Capri pants") == "tell me about Capri pants", "embedded name remains semantic request text");
        Check(NameListeningText.Request("capricorn stop") == "capricorn stop", "name prefix is not mistaken for Capri");
        Console.WriteLine("PASS native name hook checks " + count);
    }
}
