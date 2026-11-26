using System;
using System.Globalization;
using System.IO;
using CapriDesktop;

internal static class RetentionChecks
{
    internal static void Run(string work)
    {
        string folder = Path.Combine(work, "retention");
        Directory.CreateDirectory(folder);
        string settings = Path.Combine(folder, "settings.json"), chat = Path.Combine(folder, "chat.json");
        DateTime now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        string Transcript(int age) => "# Capri and you, " + now.AddDays(-age).ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)
            + "\n> Kept on this PC only, never uploaded. Delete a line or the whole file whenever you like.\n\n**19:22 You:** hello\n";
        string Day(int age) => Path.Combine(folder, now.AddDays(-age).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");
        string Chat(int age, string extra = "") => "{\"at\":\"" + now.AddDays(-age).ToString("o") + "\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]" + extra + "}";
        string known = "{\"HistoryRetention\":\"limited-v1\"}";
        File.WriteAllText(Day(366), Transcript(366));
        File.WriteAllText(chat, Chat(8));
        foreach (string value in new[] { null, "{}", "{\"KeepAllHistory\":false}", "{\"HistoryRetention\":\"limited-v2\"}",
            "{\"HistoryRetention\":true}", "{\"Nested\":" + known + "}", known.TrimEnd('}'),
            "{\"HistoryRetention\":null," + known.Substring(1), known + "{}" })
        {
            if (value != null) File.WriteAllText(settings, value);
            Retention.Sweep(settings, folder, chat, now);
            Require(File.Exists(Day(366)) && File.Exists(chat), "history removed without explicit opt-in");
        }

        File.WriteAllText(settings, known);
        string recovery = Path.Combine(folder, "chat.recovery.json");
        File.WriteAllText(recovery, Chat(8));
        File.WriteAllText(Day(365), Transcript(365));
        File.WriteAllText(Day(367), "Personal notes");
        File.WriteAllText(Day(368), Transcript(369));
        Retention.Sweep(settings, folder, chat, now);
        Require(!File.Exists(Day(366)) && !File.Exists(chat) && File.Exists(Day(365)) && File.Exists(Day(367))
            && File.Exists(Day(368)) && File.Exists(recovery), "wrong files removed after opt-in");

        foreach (string value in new[] { Chat(7), Chat(8, ",\"recovery\":{}"), Chat(8).TrimEnd('}'),
            Chat(8).Replace("\"content\":\"hello\"", "\"content\":null"), "{\"at\":\"2020-01-01\"}" })
        {
            File.WriteAllText(chat, value);
            Retention.Sweep(settings, folder, chat, now);
            Require(File.Exists(chat) && File.ReadAllText(chat) == value, "boundary or unrecognized chat removed");
        }
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(chat);
            File.CreateSymbolicLink(chat, recovery);
            Retention.Sweep(settings, folder, chat, now);
            Require(new FileInfo(chat).LinkTarget != null && File.Exists(recovery), "linked chat or its target removed");
        }
        Console.WriteLine("PASS retention preserves history by default and prunes only recognized, expired files after explicit opt-in");
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new Exception(detail);
    }
}
