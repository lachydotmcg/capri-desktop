using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CapriDesktop;

internal static class VoiceErrorChecks
{
    private static int failures;

    private static void Check(string name, string got, string want)
    {
        bool good = got == want;
        Console.WriteLine("{0} {1} [{2}]", good ? "PASS" : "FAIL", name, good ? "exact" : "got: " + got);
        if (!good) failures++;
    }

    private static void Check(string name, bool good, string shown)
    {
        Console.WriteLine("{0} {1} [{2}]", good ? "PASS" : "FAIL", name, shown);
        if (!good) failures++;
    }

    // A free loopback port held and released: nothing listens there a moment later, so a connect is refused
    // deterministically without hardcoding a well-known port somebody may occupy.
    private static string DeadAddress()
    {
        TcpListener held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        int port = ((IPEndPoint)held.LocalEndpoint).Port;
        held.Stop();
        return "http://127.0.0.1:" + port;
    }

    internal static int Run()
    {
        // The ElevenLabs composer is the voice taxonomy: every status and body lands on one plain sentence.
        Check("ElevenLabs 401 invalid_api_key -> Rejected", Voice.Explain(401, "{\"detail\":{\"message\":\"invalid_api_key\"}}"), "ElevenLabs rejected the key. Press Forget, paste a new one.");
        Check("ElevenLabs quota_exceeded -> out of credits", Voice.Explain(402, "{\"detail\":{\"status\":\"quota_exceeded\"}}"), "Out of ElevenLabs credits. Pick PREBUILT LOCAL in the Mainframe.");
        Check("ElevenLabs 429 bare -> busy", Voice.Explain(429, ""), "ElevenLabs is busy. Try again in a moment.");
        Check("ElevenLabs 451 -> Blocked", Voice.Explain(451, ""), "ElevenLabs is blocked where you are, so a new key won't help. Pick PREBUILT LOCAL in the Mainframe for her free voice.");
        Check("ElevenLabs 403 country words -> Blocked", Voice.Explain(403, "access restricted: unsupported country"), "ElevenLabs is blocked where you are, so a new key won't help. Pick PREBUILT LOCAL in the Mainframe for her free voice.");
        Check("ElevenLabs 403 silent -> Refused", Voice.Explain(403, ""), "ElevenLabs turned her voice away (error 403) without saying why. A VPN or proxy can do that. Still failing? Make a new key with Restrict key turned off.");
        Check("ElevenLabs 404 bare -> generic with hint", Voice.Explain(404, ""), "ElevenLabs error 404. Still failing? Make a new key with Restrict key turned off.");
        Check("ElevenLabs missing_permissions -> key hint", Voice.Explain(400, "{\"detail\":{\"status\":\"missing_permissions\"}}"), "Key is missing permissions. Make a new ElevenLabs key with Restrict key turned off.");
        Check("ElevenLabs voice_not_found -> add it", Voice.Explain(400, "{\"detail\":{\"status\":\"voice_not_found\"}}"), "Voice not found. Add it to your ElevenLabs voices.");
        Check("ElevenLabs library voice -> paid only", Voice.Explain(403, "{\"detail\":{\"message\":\"free_users_not_allowed\"}}"), "ElevenLabs only lets paid plans use Voice Library voices in apps. Pick a voice you made or a default one.");
        Check("ElevenLabs fine-tuning -> still training", Voice.Explain(400, "voice is still being fine-tuned"), "That voice is still training on ElevenLabs. Pick another until it's ready.");
        Check("ElevenLabs empty text -> Silent", Voice.Explain(200, "{\"detail\":{\"message\":\"empty text\"}}"), "That reply had nothing to say out loud.");
        Check("ElevenLabs account_restricted -> Restricted", Voice.Explain(403, "{\"detail\":{\"status\":\"account_restricted\"}}"), "ElevenLabs has restricted your account, so her voice can't connect. Sign in at elevenlabs.io to see why, or pick PREBUILT LOCAL in the Mainframe.");
        // End to end: the transport composes through Explain against a fake host.
        using (FakeCloud fake = new FakeCloud(Encoding.UTF8.GetBytes("{\"detail\":{\"message\":\"invalid_api_key\"}}"), "application/json", 401))
        {
            Voice.Host = fake.Address;
            Voice voice = new Voice { Key = "sk-test", VoiceId = "voice-test" };
            string got = null;
            try { voice.Speak("hi"); } catch (VoiceException error) { got = error.Message; }
            Check("ElevenLabs end to end 401", got ?? "no throw", "ElevenLabs rejected the key. Press Forget, paste a new one.");
        }
        Voice.Host = DeadAddress();
        Voice dead = new Voice { Key = "sk-test", VoiceId = "voice-test" };
        string unreachable = null;
        try { dead.Speak("hi"); } catch (VoiceException error) { unreachable = error.Message; }
        Check("ElevenLabs unreachable -> check your internet", unreachable ?? "no throw", "Can't reach ElevenLabs. Check your internet.");
        // LocalSpeaker: the address composer and the HTTP paths speak the same way; a normal return is a failure too.
        string unusable = null;
        try { LocalSpeaker.Endpoint(":::"); } catch (VoiceException error) { unusable = error.Message; }
        Check("Local server unusable address", unusable ?? "no throw", "Your local voice server's address doesn't work. Try http://localhost:8880/v1.");
        string blank = null;
        try { LocalSpeaker.Endpoint("   "); } catch (VoiceException error) { blank = error.Message; }
        Check("Local server empty address", blank ?? "no throw", "Set your local voice server's address first.");
        using (FakeCloud missing = new FakeCloud(Encoding.UTF8.GetBytes("no such route"), "text/plain", 404))
        {
            LocalSpeaker speaker = new LocalSpeaker { Url = missing.Address };
            string got = null;
            try { speaker.Speak("hi"); } catch (VoiceException error) { got = error.Message; }
            Check("Local server 404", got ?? "no throw", "Your local voice server has no /v1/audio/speech (HTTP 404). Check its address.");
        }
        using (FakeCloud broken = new FakeCloud(Encoding.UTF8.GetBytes("boom"), "text/plain", 500))
        {
            LocalSpeaker speaker = new LocalSpeaker { Url = broken.Address };
            string got = null;
            try { speaker.Speak("hi"); } catch (VoiceException error) { got = error.Message; }
            Check("Local server 500", got ?? "no throw", "Your local voice server said HTTP 500: boom");
        }
        bool shapedReady = new LocalSpeaker { Url = "http://127.0.0.1:1/v1" }.Ready;
        Check("Local server Ready accepts a shaped address", shapedReady, "Ready=" + shapedReady);
        bool malformedReady = new LocalSpeaker { Url = ":::" }.Ready;
        Check("Local server Ready refuses a malformed address", malformedReady == false, "Ready=" + malformedReady);
        string quiet = null;
        try { new QuietSpeaker().Speak("hi"); } catch (VoiceException error) { quiet = error.Message; }
        Check("Quiet speaker says it is off", quiet ?? "no throw", "Her voice provider is off.");
        return failures;
    }
}
