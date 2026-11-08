using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Capri Remote and her brains (remote/PROTOCOL.md section 8.1): a paired phone sees which brain she uses, picks another
    // and sends a brain's API key. This file is the protocol's share, with no window or brain in it, so app/RemoteTests runs
    // it too: what a phone may ask, what the PC answers, and RemoteLink's part in carrying them. Keys only ever come in: no
    // message made here can carry one out. Her side, which saves a key exactly as her Mainframe's key box does, is
    // app/RemoteBrains.cs.
    internal static class RemoteBrainKeys
    {
        // In hello-ok's features while Remote is on.
        internal const string Feature = "brains";
        internal const int MostKey = 400;
        // The rungs under BRAIN in her Mainframe, by tier: 0, 1 and 2.
        internal static readonly string[] Brains = { "normal", "medium", "max" };
        internal static readonly string[] Shown = { "NORMAL", "MEDIUM INTELLIGENCE", "MAX INTELLIGENCE" };
        // Her Mainframe's own words for a paid key typed under MEDIUM INTELLIGENCE (its paidNote; the smoke test checks they match).
        internal const string PaidNote = "That's a paid key: it goes under MAX INTELLIGENCE.";
        internal const string StillChecking = "Still checking the last key.";
        internal const string TakenOver = "Her brain changed on your PC before the key was checked.";
        internal const string TooSlow = "Couldn't check the key in time. Try again.";
        internal const string PlanHere = "Plans are picked on your PC.";

        internal static bool Owns(string type) { return type == "brain.get" || type == "brain.use" || type == "brain.key"; }

        internal static int Tier(string brain) { return brain == null ? -1 : Array.IndexOf(Brains, brain); }

        // What a phone asked, read and checked.
        internal sealed class Ask
        {
            internal string Device, Type, Id, Key;
            internal int Tier = -1;
        }

        // A phone's brain ask, or null when it's malformed: the PC answers error bad-message, with re when its id was good.
        // A key is trimmed, as her Mainframe's key box trims it, and has 1 to MostKey characters and no control characters,
        // which could never be a key and would break the header it goes out in.
        internal static Ask Read(string device, RemoteJson.Map message, out string re)
        {
            re = null;
            string type = message == null ? null : message.Text("type");
            if (!Owns(type)) return null;
            Ask ask = new Ask { Device = device, Type = type };
            if (type == "brain.get") return ask;
            ask.Tier = Tier(message.Text("brain"));
            if (type == "brain.use") return ask.Tier >= 0 ? ask : null;
            string id = message.Text("id");
            if (!RemoteMessages.IdOk(id)) return null;
            re = ask.Id = id;
            string key = (message.Text("key") ?? "").Trim();
            if (ask.Tier < 1 || key.Length == 0 || key.Length > MostKey) return null;
            foreach (char c in key) if (char.IsControl(c)) return null;
            ask.Key = key;
            return ask;
        }

        // Her brains for a phone: the one in use, how her Mainframe names it, and only whether each brain has a key.
        internal static string State(int use, string name, string note, bool medium, bool max)
        {
            bool[] keys = { false, medium, max };
            List<RemoteJson.Writer> brains = new List<RemoteJson.Writer>();
            for (int i = 0; i < Brains.Length; i++) brains.Add(new RemoteJson.Writer().Text("id", Brains[i]).Flag("key", keys[i]));
            return new RemoteJson.Writer().Text("type", "brain.state").Text("use", Brains[Math.Max(0, Math.Min(Brains.Length - 1, use))])
                .Text("name", RemoteMessages.CleanText(name, 64)).Text("note", RemoteMessages.CleanText(note, 200)).Objects("brains", brains).ToString();
        }

        // How a phone's key went: "checking", "saved" or "failed", in her Mainframe's words. The key that was sent never
        // goes back in them, whatever they say.
        internal static string Note(string re, string state, string text, string key)
        {
            return new RemoteJson.Writer().Text("type", "brain.note").Text("re", re).Text("state", state).Text("text", RemoteMessages.CleanText(Without(text, key), 200)).ToString();
        }

        internal static string Without(string text, string key)
        {
            string words = text ?? "";
            return string.IsNullOrEmpty(key) || key.Length < 8 ? words : words.Replace(key, "[key]");
        }
    }

    // RemoteLink's part: it hands a phone's brain asks on, and carries the PC's answers to that phone.
    internal sealed partial class RemoteLink
    {
        // A phone's brain ask, with its device id and the message as it came, raised outside the lock on the read thread
        // once the frame's other events are out. Must return quickly. The message can hold a key: never log it or send it on.
        internal event Action<string, RemoteJson.Map> BrainAsked;

        private readonly List<KeyValuePair<string, RemoteJson.Map>> brainAsks = new List<KeyValuePair<string, RemoteJson.Map>>();

        // hello-ok's features: the link's own, then her brains while something answers for them.
        private string[] Offered()
        {
            if (BrainAsked == null) return Features;
            string[] offered = new string[Features.Length + 1];
            Features.CopyTo(offered, 0);
            offered[Features.Length] = RemoteBrainKeys.Feature;
            return offered;
        }

        // Under gate, from OnMessage: a brain ask waits for RaiseBrainAsks. False for every other message.
        private bool BrainAsk(Phone phone, RemoteJson.Map message)
        {
            if (!RemoteBrainKeys.Owns(message.Text("type"))) return false;
            if (brainAsks.Count < 32) brainAsks.Add(new KeyValuePair<string, RemoteJson.Map>(phone.Device.Id, message));
            return true;
        }

        private void RaiseBrainAsks()
        {
            List<KeyValuePair<string, RemoteJson.Map>> asked;
            lock (gate)
            {
                if (brainAsks.Count == 0) return;
                asked = new List<KeyValuePair<string, RemoteJson.Map>>(brainAsks);
                brainAsks.Clear();
            }
            Action<string, RemoteJson.Map> heard = BrainAsked;
            if (heard == null || Stopped) return;
            foreach (KeyValuePair<string, RemoteJson.Map> item in asked)
            {
                try { heard(item.Key, item.Value); }
                catch (Exception) { }
            }
        }

        // A brain message for one phone. Only now matters, so it's dropped when that phone has no session: it asks again
        // with its next one.
        internal void SendBrain(string device, string json) { Send(device, json, null, true, null); }
    }
}
