using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CapriDesktop;

internal sealed class NamedSpeaker : ISpeaker
{
    private readonly string name;
    internal NamedSpeaker(string name) { this.name = name; }
    public string Name { get { return name; } }
    public bool Ready { get { return true; } }
    public byte[] Speak(string text) { return new byte[0]; }
}

internal sealed class FakeCloud : IDisposable
{
    private readonly TcpListener listener;
    private readonly Thread thread;
    private readonly byte[] response;
    private readonly string type;
    private readonly int status;
    internal volatile string Head = "";
    internal volatile string Body = "";

    internal FakeCloud(byte[] response, string type, int status)
    {
        this.response = response;
        this.type = type;
        this.status = status;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        thread = new Thread(Serve);
        thread.IsBackground = true;
        thread.Start();
    }

    internal string Address { get { return "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port; } }

    private static string Reason(int code)
    {
        if (code == 200) return "OK";
        if (code == 401) return "Unauthorized";
        if (code == 402) return "Payment Required";
        if (code == 403) return "Forbidden";
        if (code == 404) return "Not Found";
        if (code == 429) return "Too Many Requests";
        return "Server Error";
    }

    private void Serve()
    {
        try
        {
            using (TcpClient client = listener.AcceptTcpClient())
            using (NetworkStream stream = client.GetStream())
            {
                MemoryStream head = new MemoryStream();
                int one = -1, two = -1, three = -1;
                while (true)
                {
                    int next = stream.ReadByte();
                    if (next < 0) break;
                    head.WriteByte((byte)next);
                    if (one == 13 && two == 10 && three == 13 && next == 10) break;
                    one = two; two = three; three = next;
                }
                Head = Encoding.ASCII.GetString(head.ToArray());
                int length = 0;
                Match found = Regex.Match(Head, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                if (found.Success) length = int.Parse(found.Groups[1].Value);
                byte[] body = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int got = stream.Read(body, read, length - read);
                    if (got <= 0) break;
                    read += got;
                }
                Body = Encoding.UTF8.GetString(body, 0, read);
                byte[] reply = Encoding.UTF8.GetBytes("HTTP/1.1 " + status + " " + Reason(status) + "\r\nContent-Type: " + type + "\r\nContent-Length: " + response.Length + "\r\nConnection: close\r\n\r\n");
                stream.Write(reply, 0, reply.Length);
                stream.Write(response, 0, response.Length);
            }
        }
        catch (Exception) { }
        finally { try { listener.Stop(); } catch (Exception) { } }
    }

    public void Dispose()
    {
        try { listener.Stop(); } catch (Exception) { }
    }
}

internal static class Program
{
    private static int failures;

    private static void Check(string name, bool good, string shown)
    {
        Console.WriteLine((good ? "PASS " : "FAIL ") + name + " [" + shown + "]");
        if (!good) failures++;
    }

    private static byte[] Wav()
    {
        return Voice.Wave(new short[24000], 24000);
    }

    private static string Header(string head, string name)
    {
        Match found = Regex.Match(head, @"^" + Regex.Escape(name) + @":\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return found.Success ? found.Groups[1].Value.Trim() : null;
    }

    private static string Problem(Func<byte[]> speak)
    {
        try { speak(); return null; }
        catch (VoiceException error) { return error.Message; }
    }

    private static void ProviderChecks()
    {
        NamedSpeaker eleven = new NamedSpeaker("ElevenLabs"), fish = new NamedSpeaker("Fish Audio"), openAi = new NamedSpeaker("OpenAI");
        NamedSpeaker local = new NamedSpeaker("Local server"), owned = new NamedSpeaker("Retained local voice"), none = new NamedSpeaker("none");
        string[] shipped = { Speakers.ElevenLabs, Speakers.Fish, Speakers.OpenAi, Speakers.Local, Speakers.Owned, Speakers.Off };
        foreach (string id in shipped) Check("a saved " + id + " provider stays itself", Speakers.Known(id) == id, Speakers.Known(id) ?? "null");
        Check("an unknown provider still falls back to ElevenLabs", Speakers.Known("bogus") == Speakers.ElevenLabs, Speakers.Known("bogus") ?? "null");
        Check("no saved provider still falls back to ElevenLabs", Speakers.Known(null) == Speakers.ElevenLabs, Speakers.Known(null) ?? "null");
        Check("the picker lists every shipped provider once", Speakers.Pickable.Length == shipped.Length && new List<string>(Speakers.Pickable).TrueForAll(delegate(string id) { return Array.IndexOf(shipped, id) >= 0; }) && new List<string>(shipped).TrueForAll(delegate(string id) { return Array.IndexOf(Speakers.Pickable, id) >= 0; }), string.Join(",", Speakers.Pickable));
        Check("ElevenLabs resolves to its own speaker", Speakers.Pick(Speakers.ElevenLabs, eleven, fish, openAi, local, owned, none) == eleven, Speakers.Pick(Speakers.ElevenLabs, eleven, fish, openAi, local, owned, none).Name);
        Check("Fish Audio resolves to the Fish Audio speaker", Speakers.Pick(Speakers.Fish, eleven, fish, openAi, local, owned, none) == fish, Speakers.Pick(Speakers.Fish, eleven, fish, openAi, local, owned, none).Name);
        Check("OpenAI resolves to the OpenAI speaker", Speakers.Pick(Speakers.OpenAi, eleven, fish, openAi, local, owned, none) == openAi, Speakers.Pick(Speakers.OpenAi, eleven, fish, openAi, local, owned, none).Name);
        Check("the local server resolves to its own speaker", Speakers.Pick(Speakers.Local, eleven, fish, openAi, local, owned, none) == local, Speakers.Pick(Speakers.Local, eleven, fish, openAi, local, owned, none).Name);
        Check("the retained local voice resolves to its own speaker", Speakers.Pick(Speakers.Owned, eleven, fish, openAi, local, owned, none) == owned, Speakers.Pick(Speakers.Owned, eleven, fish, openAi, local, owned, none).Name);
        Check("off resolves to no voice", Speakers.Pick(Speakers.Off, eleven, fish, openAi, local, owned, none) == none, Speakers.Pick(Speakers.Off, eleven, fish, openAi, local, owned, none).Name);
        Check("an unknown saved provider still gets ElevenLabs", Speakers.Pick("bogus", eleven, fish, openAi, local, owned, none) == eleven, Speakers.Pick("bogus", eleven, fish, openAi, local, owned, none).Name);
        Check("the provider menu names every provider", Speakers.PickName(Speakers.ElevenLabs, "Local server", "Retained local voice") == "ElevenLabs" && Speakers.PickName(Speakers.Fish, "Local server", "Retained local voice") == "Fish Audio (cloud)"
            && Speakers.PickName(Speakers.OpenAi, "Local server", "Retained local voice") == "OpenAI (cloud)" && Speakers.PickName(Speakers.Local, "Local server", "Retained local voice") == "Local server (OpenAI speech API)"
            && Speakers.PickName(Speakers.Owned, "Local server", "Retained local voice") == "Retained local voice" && Speakers.PickName(Speakers.Off, "Local server", "Retained local voice") == "Off", Speakers.PickName(Speakers.Fish, "Local server", "Retained local voice"));
        Check("the provider heading names every provider", Speakers.ShortName(Speakers.ElevenLabs, "Local server", "Retained local voice") == "ElevenLabs" && Speakers.ShortName(Speakers.Fish, "Local server", "Retained local voice") == "Fish Audio"
            && Speakers.ShortName(Speakers.OpenAi, "Local server", "Retained local voice") == "OpenAI" && Speakers.ShortName(Speakers.Local, "Local server", "Retained local voice") == "Local server"
            && Speakers.ShortName(Speakers.Owned, "Local server", "Retained local voice") == "Retained local voice" && Speakers.ShortName(Speakers.Off, "Local server", "Retained local voice") == "Off", Speakers.ShortName(Speakers.OpenAi, "Local server", "Retained local voice"));
    }

    private static void CueChecks()
    {
        Check("Fish Audio keeps her cues", Speakers.FishCues(Speakers.Fish), Speakers.FishCues(Speakers.Fish).ToString());
        Check("every other provider drops her cues", !Speakers.FishCues(Speakers.ElevenLabs) && !Speakers.FishCues(Speakers.Local) && !Speakers.FishCues(Speakers.OpenAi) && !Speakers.FishCues(Speakers.Off), Speakers.FishCues(Speakers.ElevenLabs).ToString());
        Check("Fish Audio gets the cued reply and every other voice the plain one", Speakers.Words(Speakers.Fish, "Hey choom.", "[laughing] Hey choom.") == "[laughing] Hey choom."
            && Speakers.Words(Speakers.ElevenLabs, "Hey choom.", "[laughing] Hey choom.") == "Hey choom." && Speakers.Words(Speakers.OpenAi, "Hey choom.", "[laughing] Hey choom.") == "Hey choom.", Speakers.Words(Speakers.Fish, "Hey choom.", "[laughing] Hey choom."));
        Check("her cue stays in a line Fish Audio says and comes out of every other one", Voice.Spoken("[laughing] Hey choom.", true) == "[laughing] Hey choom." && Voice.Spoken("[laughing] Hey choom.", false) == "Hey choom.", Voice.Spoken("[laughing] Hey choom.", true));
        Check("only Fish Audio's own cue names count", Voice.Cue.IsMatch("[laughing] Hey") && Voice.Cue.IsMatch("(sad) Hey") && !Voice.Cue.IsMatch("[GLITCH] Hey"), "[laughing]=" + Voice.Cue.IsMatch("[laughing] Hey") + " (sad)=" + Voice.Cue.IsMatch("(sad) Hey") + " [GLITCH]=" + Voice.Cue.IsMatch("[GLITCH] Hey"));
    }

    private static void AddressChecks()
    {
        Check("Fish Audio's address ends at /v1/tts", FishSpeaker.Endpoint("https://api.fish.audio").ToString() == "https://api.fish.audio/v1/tts"
            && FishSpeaker.Endpoint("https://api.fish.audio/v1").ToString() == "https://api.fish.audio/v1/tts" && FishSpeaker.Endpoint("https://api.fish.audio/v1/tts").ToString() == "https://api.fish.audio/v1/tts", FishSpeaker.Endpoint("https://api.fish.audio").ToString());
        Check("OpenAI's address ends at /v1/audio/speech", OpenAiSpeaker.Endpoint("https://api.openai.com/v1").ToString() == "https://api.openai.com/v1/audio/speech"
            && OpenAiSpeaker.Endpoint("https://api.openai.com/v1/audio/speech").ToString() == "https://api.openai.com/v1/audio/speech", OpenAiSpeaker.Endpoint("https://api.openai.com/v1").ToString());
        Check("a bare PC address gets http and its speech path", FishSpeaker.Endpoint("127.0.0.1:8880").ToString() == "http://127.0.0.1:8880/v1/tts" && OpenAiSpeaker.Endpoint("127.0.0.1:8880").ToString() == "http://127.0.0.1:8880/v1/audio/speech", FishSpeaker.Endpoint("127.0.0.1:8880").ToString() + " / " + OpenAiSpeaker.Endpoint("127.0.0.1:8880").ToString());
        bool bad = false;
        try { FishSpeaker.Endpoint("ftp://x"); } catch (VoiceException) { bad = true; }
        Check("a non-HTTP address is refused", bad, bad.ToString());
    }

    private static void FishChecks()
    {
        string id = "0123456789abcdef0123456789abcdef";
        Check("a fish.audio link becomes its voice ID", FishSpeaker.VoiceId("https://fish.audio/m/" + id + "/") == id && FishSpeaker.VoiceId(id) == id && FishSpeaker.VoiceId("Capri Default") == "" && FishSpeaker.VoiceId(null) == "", FishSpeaker.VoiceId("https://fish.audio/m/" + id + "/"));
        using (FakeCloud fake = new FakeCloud(Wav(), "audio/wav", 200))
        {
            FishSpeaker fish = new FishSpeaker { Key = "fish-key-1234", Reference = "https://fish.audio/m/" + id, Url = fake.Address };
            byte[] said = fish.Speak("[laughing] Hey choom & co.");
            Check("Fish Audio's request matches Windows", fake.Head.StartsWith("POST /v1/tts HTTP/1.1", StringComparison.Ordinal) && Header(fake.Head, "Authorization") == "Bearer fish-key-1234"
                && Header(fake.Head, "model") == "s2.1-pro-free" && fake.Body == "{\"text\":\"[laughing] Hey chewm and co.\",\"reference_id\":\"" + id + "\",\"format\":\"wav\",\"sample_rate\":24000}",
                fake.Head.Split('\n')[0].Trim() + " model=" + Header(fake.Head, "model") + " body=" + fake.Body);
            Check("Fish Audio's WAV comes back as 16-bit mono", Voice.Seconds(said) > 0.99 && Voice.Seconds(said) < 1.01, Voice.Seconds(said).ToString("0.00"));
        }
        using (FakeCloud fake = new FakeCloud(Encoding.UTF8.GetBytes("{\"message\":\"bad key\"}"), "application/json", 401))
        {
            FishSpeaker fish = new FishSpeaker { Key = "fish-key-1234", Url = fake.Address };
            string problem = Problem(delegate { return fish.Speak("Hey."); });
            Check("Fish Audio's refused key says so", problem == "Fish Audio turned down its key (HTTP 401).", problem ?? "none");
        }
        using (FakeCloud fake = new FakeCloud(Encoding.UTF8.GetBytes("{\"detail\":\"not found\"}"), "application/json", 404))
        {
            FishSpeaker fish = new FishSpeaker { Key = "fish-key-1234", Url = fake.Address };
            string problem = Problem(delegate { return fish.Speak("Hey."); });
            Check("Fish Audio's missing voice says where to look", problem == "Fish Audio can't find that voice (HTTP 404). Check its ID or link on fish.audio.", problem ?? "none");
        }
        using (FakeCloud fake = new FakeCloud(Encoding.UTF8.GetBytes("{\"message\":\"bad request\"}"), "application/json", 200))
        {
            FishSpeaker fish = new FishSpeaker { Key = "fish-key-1234", Url = fake.Address };
            string problem = Problem(delegate { return fish.Speak("Hey."); });
            Check("Fish Audio's words instead of sound are said plainly", problem == "Fish Audio sent words, not sound: bad request", problem ?? "none");
        }
        using (FakeCloud fake = new FakeCloud(new byte[0], "audio/wav", 200))
        {
            FishSpeaker fish = new FishSpeaker { Key = "fish-key-1234", Url = fake.Address };
            string problem = Problem(delegate { return fish.Speak("Hey."); });
            Check("Fish Audio's empty reply is said plainly", problem == "Fish Audio sent back no sound.", problem ?? "none");
        }
        FishSpeaker unkeyed = new FishSpeaker { Url = "http://127.0.0.1:1" };
        Check("Fish Audio asks for its key first", !unkeyed.Ready && Problem(delegate { return unkeyed.Speak("Hey."); }) == "Paste your Fish Audio key first.", Problem(delegate { return unkeyed.Speak("Hey."); }) ?? "none");
    }

    private static void OpenAiChecks()
    {
        using (FakeCloud fake = new FakeCloud(Wav(), "audio/wav", 200))
        {
            OpenAiSpeaker openAi = new OpenAiSpeaker { Key = "sk-key-1234", Url = fake.Address };
            byte[] said = openAi.Speak("[laughing] Hey choom.");
            Check("OpenAI's request matches Windows", fake.Head.StartsWith("POST /v1/audio/speech HTTP/1.1", StringComparison.Ordinal) && Header(fake.Head, "Authorization") == "Bearer sk-key-1234" && Header(fake.Head, "model") == null
                && fake.Body == "{\"model\":\"gpt-4o-mini-tts\",\"input\":\"Hey chewm.\",\"voice\":\"marin\",\"response_format\":\"wav\"}", fake.Head.Split('\n')[0].Trim() + " body=" + fake.Body);
            Check("OpenAI's WAV comes back as 16-bit mono", Voice.Seconds(said) > 0.99 && Voice.Seconds(said) < 1.01, Voice.Seconds(said).ToString("0.00"));
        }
        using (FakeCloud fake = new FakeCloud(Wav(), "audio/wav", 200))
        {
            OpenAiSpeaker openAi = new OpenAiSpeaker { Key = "sk-key-1234", VoiceName = "cedar", Url = fake.Address };
            openAi.Speak("Hey.");
            Check("OpenAI's chosen voice is asked for", fake.Body == "{\"model\":\"gpt-4o-mini-tts\",\"input\":\"Hey.\",\"voice\":\"cedar\",\"response_format\":\"wav\"}", fake.Body);
        }
        using (FakeCloud fake = new FakeCloud(Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"You exceeded your current quota\",\"type\":\"insufficient_quota\"}}"), "application/json", 429))
        {
            OpenAiSpeaker openAi = new OpenAiSpeaker { Key = "sk-key-1234", Url = fake.Address };
            string problem = Problem(delegate { return openAi.Speak("Hey."); });
            Check("OpenAI's spent credit says so", problem == "OpenAI is out of credit. Add credit at platform.openai.com, or pick another voice in my right-click menu under VOICE.", problem ?? "none");
        }
        OpenAiSpeaker unkeyed = new OpenAiSpeaker { Url = "http://127.0.0.1:1" };
        Check("OpenAI asks for its key first", !unkeyed.Ready && Problem(delegate { return unkeyed.Speak("Hey."); }) == "Paste your OpenAI key first.", Problem(delegate { return unkeyed.Speak("Hey."); }) ?? "none");
    }

    private static void KeyChecks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "capri-cross-voice-" + Guid.NewGuid().ToString("N"));
        try
        {
            FishSpeaker fish = new FishSpeaker();
            bool saved = fish.SaveKey(folder, "  fish-key-1234  ");
            string path = Path.Combine(folder, "voice-fish-key");
            Check("Fish Audio's key is stored in its own file by the shared secret helper", saved && fish.Key == "fish-key-1234" && File.Exists(path) && File.ReadAllText(path) == "fish-key-1234" && FishSpeaker.ReadKey(folder) == "fish-key-1234", saved + " key=" + fish.Key + " file=" + (File.Exists(path) ? File.ReadAllText(path) : "missing"));
            if (!OperatingSystem.IsWindows())
                Check("Fish Audio's key file is readable only by them", File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), File.GetUnixFileMode(path).ToString());
            Check("Fish Audio's key can be removed", fish.ForgetKey(folder) && fish.Key == null && !File.Exists(path) && FishSpeaker.ReadKey(folder) == null, fish.Key ?? "null");
            Check("an empty Fish Audio key is not stored", !fish.SaveKey(folder, "   ") && !File.Exists(path), File.Exists(path).ToString());

            OpenAiSpeaker openAi = new OpenAiSpeaker();
            bool openSaved = openAi.SaveKey(folder, "sk-key-5678");
            string openPath = Path.Combine(folder, "voice-openai-key");
            Check("OpenAI's key is stored in its own file by the shared secret helper", openSaved && openAi.Key == "sk-key-5678" && File.Exists(openPath) && File.ReadAllText(openPath) == "sk-key-5678" && OpenAiSpeaker.ReadKey(folder) == "sk-key-5678", openSaved + " key=" + openAi.Key + " file=" + (File.Exists(openPath) ? File.ReadAllText(openPath) : "missing"));
            Check("OpenAI's key can be removed", openAi.ForgetKey(folder) && openAi.Key == null && !File.Exists(openPath) && OpenAiSpeaker.ReadKey(folder) == null, openAi.Key ?? "null");
            Check("an empty OpenAI key is not stored", !openAi.SaveKey(folder, "") && !File.Exists(openPath), File.Exists(openPath).ToString());
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }

    private static int Main()
    {
        ProviderChecks();
        CueChecks();
        AddressChecks();
        FishChecks();
        OpenAiChecks();
        KeyChecks();
        Console.WriteLine(failures == 0 ? "TOTAL PASS" : "TOTAL FAIL " + failures);
        return failures == 0 ? 0 : 1;
    }
}
