using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using CapriDesktop;
using CapriDesktop.Services;
internal static class StabilityTests
{
    private static int count;
    private static void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
    private static void Reject(Action operation, string message)
    { bool rejected = false; try { operation(); } catch (InvalidDataException) { rejected = true; } catch (ArgumentException) { rejected = true; } Check(rejected, message); }
    internal static void Run()
    {
        var defaults = ChatJson.Read<ChatConfiguration>(System.Text.Encoding.UTF8.GetBytes("{}")); defaults.Validate();
        Check(defaults.HistoryMessages == 80 && defaults.FontSize == 11 && !defaults.PersistHistory, "Partial chat settings default safely");
        var character = ChatJson.Read<Preferences>(System.Text.Encoding.UTF8.GetBytes("{\"Size\":-500,\"X\":2147483647,\"HasPosition\":true}")); character.Normalize();
        Check(character.Size == 240 && character.Motion && character.OnTop && !character.HasPosition && character.MovementSpeed == 50, "Character settings normalize safely");
        Check(RuntimePolicy.Clamp(new Point(100, 100), new Size(320, 320), new Rectangle(-1920, -300, 1920, 1080)) == new Point(-320, 100), "Negative-monitor coordinates");
        Check(RuntimePolicy.Clamp(new Point(int.MaxValue, int.MinValue), new Size(1000, 1000), new Rectangle(0, 0, 800, 600)) == Point.Empty, "Oversized window bounded without overflow");
        Check(!RuntimePolicy.SocialAnimationAllowed(false, false, false, true, false), "Flight takes priority over chat animation");
        Check(!RuntimePolicy.SocialAnimationAllowed(true, false, false, false, false), "Rest takes priority over chat animation");
        Check(!RuntimePolicy.SocialAnimationAllowed(false, false, false, false, true), "Suspension blocks chat animation");
        Check(RuntimePolicy.SocialAnimationAllowed(false, false, false, false, false), "Idle allows social animation");
        Check(Math.Abs(RuntimePolicy.CpuPercent(1, 1, 4) - 25) < .001 && RuntimePolicy.CpuPercent(1, 0, 4) == 0, "CPU diagnostics denominator");
        Reject(delegate { AssetCatalog.ValidatePng(null, 16, "missing"); }, "Missing asset rejected");
        Reject(delegate { using (var stream = new MemoryStream(new byte[24])) AssetCatalog.ValidatePng(stream, 16, "bad"); }, "Invalid PNG rejected");
        byte[] png = {137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,1,0,0,0,0,64};
        using (var stream = new MemoryStream(png)) AssetCatalog.ValidatePng(stream, 16, "valid");
        png[19] = 1;
        Reject(delegate { using (var stream = new MemoryStream(png)) AssetCatalog.ValidatePng(stream, 16, "bad strip"); }, "Indivisible sprite strip rejected");
        string root = Path.Combine(Path.GetTempPath(), "capri-stability-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var storage = new ChatStorage(root); var config = new ChatConfiguration { PersistHistory = true };
            var sessions = new List<ConversationSession> { new ConversationSession() };
            sessions[0].Messages.Add(new ConversationMessage("user", "saved")); storage.SaveSessions(sessions, config, false);
            byte[] before = File.ReadAllBytes(Path.Combine(root, "conversations.json"));
            sessions[0].Messages.Add(new ConversationMessage("user", "private content")); storage.SaveSessions(sessions, config, true);
            Check(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "conversations.json"))), "Private saves leave existing archive byte-for-byte unchanged");
            sessions[0].Id = null; File.WriteAllBytes(Path.Combine(root, "conversations.json"), ChatJson.Write(sessions));
            Reject(delegate { storage.LoadSessions(); }, "Invalid session identity rejected");
            string path = Path.Combine(root, "conversations.json"); File.WriteAllText(path, "null");
            Reject(delegate { storage.SaveSessions(new List<ConversationSession>(), config, false); }, "Damaged archive not overwritten");
            Check(File.ReadAllText(path) == "null", "Corrupt archive preserved for recovery");
            storage.DeleteAll(); Check(storage.LoadSessions().Count == 0, "Explicit recovery deletion works");
            File.WriteAllBytes(path, new byte[8 * 1024 * 1024 + 1]);
            Reject(delegate { storage.LoadSessions(); }, "Oversized archive rejected before parse");
            config.GreetingEnglish = " |  | "; Reject(config.Validate, "Empty response variants rejected");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS " + count + " stability checks: assets, geometry, settings migration, animation priority, diagnostics, privacy and corrupt-data recovery");
    }
}
