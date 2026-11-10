using System;
using System.Collections.Generic;
using System.Reflection;
using CapriDesktop;

internal static class ToolLifecycleChecks
{
    private sealed class FixtureProvider : IBrainProvider
    {
        internal KeyValuePair<string, ToolArguments>? Next;
        public string Identity => "isolated-lifecycle-fixture";
        public int Generation => 0;
        public void Cancel() { }
        public string Complete(string model, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose, string parts = null)
        {
            if (Next.HasValue) { calls.Add(Next.Value); Next = null; return ""; }
            return "Fixture complete.";
        }
    }

    internal static void Run()
    {
        var originalRoute = Hands.NativeRequest;
        int checks = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        try
        {
            foreach (bool planned in new[] { false, true })
            foreach (string tool in new[] { "open_app", "play_music", "stream", "linux_system", "run_terminal", "terminal_job", "look_at_screen" })
            foreach (bool ownedGuard in new[] { false, true })
            {
                bool terminal = tool == "run_terminal" || tool == "terminal_job";
                bool requested = terminal || tool == "look_at_screen";
                Func<bool> ownerCurrent = ownedGuard ? () => true : null;
                var args = new ToolArguments { Name="fixture", Song="fixture", Action="status", Command="printf fixture", JobId="0123456789abcdef0123456789abcdef", Current=ownerCurrent, Focus=tool == "look_at_screen" ? "read the public fixture" : null, Screen=tool == "look_at_screen" ? "2" : null, Asked=tool == "look_at_screen" ? "earlier proposal text" : null };
                if (tool == "terminal_job") args.Action="read";
                if (tool == "stream") args.Action="start";
                var call = new KeyValuePair<string, ToolArguments>(tool, args);
                var provider = new FixtureProvider();
                var brain = new Brain { Model="fixture", Provider=provider };
                int dispatched = 0;
                brain.Act = (name, proposed) => { if (name != tool || !ReferenceEquals(args, proposed)) throw new Exception("Unexpected fixture dispatch"); dispatched++; return "Fixture result."; };
                Hands.NativeRequest = _ => call;
                if (planned)
                {
                    provider.Next = call;
                    // -1 is the established unchecked ticket used by older call paths.
                    // Exercise that sentinel for legacy tools and a live ticket for request-scoped tools.
                    typeof(Brain).GetMethod("WorkOut", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(brain,
                        new object[] { "fixture", new List<ChatMessage>(), "fixture request", new List<KeyValuePair<string, ToolArguments>>(), new List<string>(), new List<string>(), 200, requested ? 0 : -1, Hands.KitFor() });
                }
                else brain.Ask("fixture request");
                string label = (planned ? "planned" : "direct") + " " + tool + " " + (ownedGuard ? "owned guard" : "no guard");
                Check(dispatched == 1, label + " dispatches exactly once");
                if (tool == "look_at_screen") Check(args.Asked == "fixture request" && args.Focus == "read the public fixture" && args.Screen == "2", label + " preserves original owner request and parsed visual arguments");
                Check(requested ? args.Current != null && args.Current() : ReferenceEquals(args.Current, ownerCurrent), label + " preserves the correct lifecycle owner");
                brain.Cancel();
                Check(requested ? !args.Current() : ReferenceEquals(args.Current, ownerCurrent) && (args.Current == null || args.Current()), label + " cancellation affects only request-scoped guard");
            }
        }
        finally { Hands.NativeRequest = originalRoute; }
        Console.WriteLine("PASS tool lifecycle total=" + checks);
    }
}
