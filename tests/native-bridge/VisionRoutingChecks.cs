using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CapriDesktop;

internal static class VisionRoutingChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        using JsonDocument compiled = JsonDocument.Parse(Hands.KitFor().Tools);
        JsonElement look = compiled.RootElement.EnumerateArray().Single(row => row.GetProperty("function").GetProperty("name").GetString() == "look_at_screen").GetProperty("function");
        JsonElement parameters = look.GetProperty("parameters");
        Check(parameters.GetProperty("properties").GetProperty("screen").GetProperty("type").GetString() == "string", "compiled CROSS look schema advertises a string monitor selector");
        Check(!parameters.TryGetProperty("required", out JsonElement required) || !required.EnumerateArray().Any(row => row.GetString() == "screen"), "compiled look schema keeps screen optional for older callers");
        var calls = new List<KeyValuePair<string, ToolArguments>>();
        PlanBrain.Proposals("{\"text\":\"\",\"calls\":[{\"name\":\"look_at_screen\",\"arguments\":{\"focus\":\"solve the public puzzle\",\"screen\":\"2\"}}]}", compiled.RootElement.GetRawText(), calls);
        Check(calls.Count == 1 && calls[0].Key == "look_at_screen" && calls[0].Value.Screen == "2" && calls[0].Value.Focus == "solve the public puzzle", "typed proposal retains monitor and visual question from the compiled schema");
        calls.Clear();
        PlanBrain.Proposals("{\"text\":\"\",\"calls\":[{\"name\":\"look_at_screen\",\"arguments\":{\"focus\":\"read the public fixture\"}}]}", compiled.RootElement.GetRawText(), calls);
        Check(calls.Count == 1 && calls[0].Value.Screen == null, "older typed look proposal without screen remains valid");
        calls.Clear();
        bool invalidScreen = false;
        try { PlanBrain.Proposals("{\"text\":\"\",\"calls\":[{\"name\":\"look_at_screen\",\"arguments\":{\"screen\":2}}]}", compiled.RootElement.GetRawText(), calls); }
        catch (BrainException) { invalidScreen = true; }
        Check(invalidScreen && calls.Count == 0, "numeric JSON screen cannot bypass the declared string selector contract");

        var oldNative = Hands.NativeLook;
        var oldLook = Hands.Look;
        var oldLookOn = Hands.LookOn;
        var oldRequest = Hands.NativeRequest;
        int nativeCalls = 0, legacyCalls = 0, selectedLegacyCalls = 0;
        ToolArguments delivered = null;
        try
        {
            Hands.NativeRequest = null;
            Hands.Look = focus => { legacyCalls++; return "legacy:" + focus; };
            Hands.LookOn = (focus, screen) => { selectedLegacyCalls++; return "legacy-selected:" + focus + ":" + screen; };
            Hands.NativeLook = args => { nativeCalls++; delivered = args; return "native-public-fixture"; };
            Func<bool> current = () => true;
            var args = new ToolArguments { Focus="read public board", Screen="2", Asked="original owner request", Current=current };
            Check(Hands.Do("look_at_screen", args) == "native-public-fixture" && ReferenceEquals(delivered, args) && nativeCalls == 1 && legacyCalls == 0 && selectedLegacyCalls == 0,
                "native look dispatch receives the original typed arguments exactly once");
            Check(delivered.Asked == "original owner request" && delivered.Screen == "2" && delivered.Focus == "read public board" && ReferenceEquals(delivered.Current, current),
                "native look dispatch preserves task, monitor, focus and cancellation owner");
            var unqualified = new ToolArguments { Focus="read public text" };
            Check(Hands.Do("look_at_screen", unqualified) == "native-public-fixture" && ReferenceEquals(delivered, unqualified) && nativeCalls == 2,
                "native look dispatch handles older calls with no monitor field");

            Hands.NativeLook = null;
            Check(Hands.Do("look_at_screen", new ToolArguments { Focus="public text" }) == "legacy:public text" && legacyCalls == 1 && selectedLegacyCalls == 0,
                "legacy Look fallback remains valid when no native handler is attached");
            Check(Hands.Do("look_at_screen", new ToolArguments { Focus="public puzzle", Screen="2" }) == "legacy-selected:public puzzle:2" && legacyCalls == 1 && selectedLegacyCalls == 1,
                "legacy LookOn fallback retains its monitor when no native handler is attached");
            Check(Hands.Do("look_at_screen", new ToolArguments { Focus="public default", Screen=" " }) == "legacy:public default" && legacyCalls == 2,
                "blank legacy screen retains default Look behavior");
            Check(Hands.Heard("Look at my second monitor", out string tool, out ToolArguments parsed) && tool == "look_at_screen" && parsed.Screen == "second",
                "compiled ordinary monitor request reaches read-only look with its ordinal");
            Check(Hands.Heard("What's on my left screen?", out tool, out parsed) && tool == "look_at_screen" && parsed.Screen == "left",
                "compiled ordinary monitor question preserves its physical-side selector");
        }
        finally
        {
            Hands.NativeLook = oldNative; Hands.Look = oldLook; Hands.LookOn = oldLookOn; Hands.NativeRequest = oldRequest;
        }
        Console.WriteLine("PASS vision routing total=" + checks);
    }
}
