using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;

namespace CapriDesktop
{
    internal sealed record HyprlandOutput(int Id, string Name, Rect Logical, int PixelWidth, int PixelHeight,
        double Scale, int Transform, int Workspace, int SpecialWorkspace, bool Focused, Rect WorkingArea);

    internal sealed record HyprlandWindow(long Address, int Pid, string AppId, string Title, Rect Logical,
        int Monitor, int Workspace, string WorkspaceName, bool Hidden, bool Pinned, bool Floating,
        int Fullscreen, int FocusOrder, int GroupSize, bool? XWayland = null)
    {
        internal string Selector => "address:0x" + Address.ToString("x", CultureInfo.InvariantCulture);
    }

    internal sealed record HyprlandSnapshot(string SessionId, string LayoutId, HyprlandOutput[] Outputs,
        HyprlandWindow[] Windows, Point Pointer, long ActiveAddress, bool Locked, long CapturedAt)
    {
        internal bool Visible(HyprlandWindow window)
        {
            HyprlandOutput output = Array.Find(Outputs, m => m.Id == window.Monitor);
            return !Locked && output != null && !window.Hidden && (window.Pinned ||
                (output.SpecialWorkspace != 0 ? window.Workspace == output.SpecialWorkspace : window.Workspace == output.Workspace));
        }
    }

    internal static class HyprlandJson
    {
        internal static long Address(string address)
        {
            if (address == "0x0" || address == "0") return 0;
            if (address == null || !address.StartsWith("0x", StringComparison.Ordinal) || address.Length > 18 ||
                !long.TryParse(address.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long value) || value <= 0)
                throw new FormatException("Hyprland returned an invalid window address.");
            return value;
        }

        internal static string String(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out JsonElement v) ? v.GetString() ?? fallback : fallback;
        internal static int Int(JsonElement row, string key, int fallback = 0) => row.TryGetProperty(key, out JsonElement v) ? v.GetInt32() : fallback;
        internal static bool Bool(JsonElement row, string key, bool fallback = false) => row.TryGetProperty(key, out JsonElement v) ? v.GetBoolean() : fallback;
        private static int Workspace(JsonElement row, string key) => row.TryGetProperty(key, out JsonElement v) ? Int(v, "id") : 0;
        private static double Number(JsonElement row, string key)
        {
            double value = row.GetProperty(key).GetDouble();
            if (!double.IsFinite(value) || Math.Abs(value) > 1000000) throw new FormatException("Invalid desktop coordinate.");
            return value;
        }
        private static Point Pair(JsonElement row, string key)
        {
            JsonElement array = row.GetProperty(key);
            if (array.GetArrayLength() != 2) throw new FormatException("Invalid desktop coordinate pair.");
            double x = array[0].GetDouble(), y = array[1].GetDouble();
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1000000 || Math.Abs(y) > 1000000)
                throw new FormatException("Invalid desktop coordinate.");
            return new Point(x, y);
        }

        internal static HyprlandOutput[] Outputs(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() > 64) throw new FormatException("Too many desktop outputs.");
            List<HyprlandOutput> outputs = new List<HyprlandOutput>();
            foreach (JsonElement row in doc.RootElement.EnumerateArray())
            {
                if (Bool(row, "disabled") || !Bool(row, "dpmsStatus", true) || String(row, "mirrorOf", "none") != "none") continue;
                int width = Int(row, "width"), height = Int(row, "height"), transform = Int(row, "transform");
                double scale = Number(row, "scale");
                if (width <= 0 || height <= 0 || width > 65536 || height > 65536 || scale < 0.25 || scale > 8 || transform < 0 || transform > 7)
                    throw new FormatException("Invalid output size, scale or transform.");
                // IPC x/y and client at/size are already oriented logical coordinates.
                // Rotation changes the native pixel extent; do not rotate these coordinates again.
                Rect logical = new Rect(Number(row, "x"), Number(row, "y"), (transform % 2 == 0 ? width : height) / scale, (transform % 2 == 0 ? height : width) / scale);
                Rect working = logical;
                if (row.TryGetProperty("reserved", out JsonElement reserved) && reserved.GetArrayLength() == 4)
                {
                    double left = reserved[0].GetDouble(), top = reserved[1].GetDouble(), right = reserved[2].GetDouble(), bottom = reserved[3].GetDouble();
                    if (!double.IsFinite(left + top + right + bottom) || Math.Min(Math.Min(left, top), Math.Min(right, bottom)) < 0 || left + right >= logical.Width || top + bottom >= logical.Height)
                        throw new FormatException("Invalid output reserved area.");
                    working = new Rect(logical.X + left, logical.Y + top, logical.Width - left - right, logical.Height - top - bottom);
                }
                outputs.Add(new HyprlandOutput(Int(row, "id", -1), String(row, "name"), logical, width, height, scale, transform,
                    Workspace(row, "activeWorkspace"), Workspace(row, "specialWorkspace"), Bool(row, "focused"), working));
            }
            if (outputs.Any(m => m.Id < 0 || string.IsNullOrEmpty(m.Name)) || outputs.Select(m => m.Id).Distinct().Count() != outputs.Count || outputs.Select(m => m.Name).Distinct().Count() != outputs.Count)
                throw new FormatException("Ambiguous output identity.");
            return outputs.ToArray();
        }

        internal static HyprlandWindow[] Windows(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() > 2048) throw new FormatException("Too many desktop windows.");
            List<HyprlandWindow> windows = new List<HyprlandWindow>();
            foreach (JsonElement row in doc.RootElement.EnumerateArray())
            {
                if (!Bool(row, "mapped")) continue;
                Point at = Pair(row, "at"), size = Pair(row, "size");
                if (size.X <= 0 || size.Y <= 0) continue;
                JsonElement workspace = row.GetProperty("workspace");
                windows.Add(new HyprlandWindow(Address(String(row, "address")), Int(row, "pid"), String(row, "class"), String(row, "title"),
                    new Rect(at.X, at.Y, size.X, size.Y), Int(row, "monitor", -1), Int(workspace, "id"), String(workspace, "name"),
                    Bool(row, "hidden"), Bool(row, "pinned"), Bool(row, "floating"), Int(row, "fullscreen"), Int(row, "focusHistoryID", int.MaxValue),
                    row.TryGetProperty("grouped", out JsonElement grouped) ? grouped.GetArrayLength() : 0,
                    row.TryGetProperty("xwayland", out JsonElement xwayland) && (xwayland.ValueKind == JsonValueKind.True || xwayland.ValueKind == JsonValueKind.False) ? xwayland.GetBoolean() : (bool?)null));
            }
            if (windows.Any(w => w.Address == 0 || w.Pid <= 0) || windows.Select(w => w.Address).Distinct().Count() != windows.Count)
                throw new FormatException("Ambiguous window identity.");
            return windows.ToArray();
        }

        internal static Point Pointer(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return new Point(Number(doc.RootElement, "x"), Number(doc.RootElement, "y"));
        }
        internal static long Active(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string address = String(doc.RootElement, "address");
            return address.Length == 0 ? 0 : Address(address);
        }
        internal static bool Locked(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.True || doc.RootElement.ValueKind == JsonValueKind.False) return doc.RootElement.GetBoolean();
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("locked", out JsonElement locked)) return locked.GetBoolean();
            throw new FormatException("Hyprland lock state is unavailable.");
        }
        internal static string LayoutId(IEnumerable<HyprlandOutput> outputs)
        {
            // Includes workspaces and reserved areas: a handle cannot cross a workspace/layout transition.
            string canonical = JsonSerializer.Serialize(outputs.OrderBy(m => m.Id).Select(m => new { m.Id, m.Name, X = m.Logical.X, Y = m.Logical.Y,
                W = m.Logical.Width, H = m.Logical.Height, m.Scale, m.Transform, m.Workspace, m.SpecialWorkspace,
                WorkX = m.WorkingArea.X, WorkY = m.WorkingArea.Y, WorkW = m.WorkingArea.Width, WorkH = m.WorkingArea.Height }));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
    }

    // An output-specific affine map to Avalonia/XRandR pixels. No global DPI multiplier:
    // XWayland may have forced zero scaling and a different arrangement from Wayland.
    internal sealed record HyprlandDisplayMap(HyprlandOutput Output, PixelRect AvaloniaBounds)
    {
        internal Point ToAvaloniaPoint(Point point) => new Point(
            AvaloniaBounds.X + (point.X - Output.Logical.X) * AvaloniaBounds.Width / Output.Logical.Width,
            AvaloniaBounds.Y + (point.Y - Output.Logical.Y) * AvaloniaBounds.Height / Output.Logical.Height);
        internal Point ToLogical(Point point) => new Point(
            Output.Logical.X + (point.X - AvaloniaBounds.X) * Output.Logical.Width / AvaloniaBounds.Width,
            Output.Logical.Y + (point.Y - AvaloniaBounds.Y) * Output.Logical.Height / AvaloniaBounds.Height);
        internal PixelPoint ToAvalonia(Point point) => new PixelPoint(
            (int)Math.Round(AvaloniaBounds.X + (point.X - Output.Logical.X) * AvaloniaBounds.Width / Output.Logical.Width),
            (int)Math.Round(AvaloniaBounds.Y + (point.Y - Output.Logical.Y) * AvaloniaBounds.Height / Output.Logical.Height));
        internal Point ToLogical(PixelPoint point) => new Point(
            Output.Logical.X + (point.X - AvaloniaBounds.X) * Output.Logical.Width / AvaloniaBounds.Width,
            Output.Logical.Y + (point.Y - AvaloniaBounds.Y) * Output.Logical.Height / AvaloniaBounds.Height);
        internal PixelRect ToAvalonia(Rect rect)
        {
            PixelPoint a = ToAvalonia(rect.TopLeft), b = ToAvalonia(rect.BottomRight);
            return new PixelRect(a.X, a.Y, Math.Max(0, b.X - a.X), Math.Max(0, b.Y - a.Y));
        }
    }
}
