using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Explicit screen requests select one live output. Unknown words never mean "the next one".
    internal static class ScreenSelection
    {
        private const string Alias = @"(?:first|second|third|fourth|fifth|1st|2nd|3rd|4th|5th|[1-9][0-9]?|leftmost|rightmost|topmost|bottommost|left|right|top|bottom|upper|lower|up|down|main|primary|other|another|here|current|this|focused|active|portrait|vertical|next|previous)";
        private static readonly Regex Before = new Regex(@"\b(?<pick>" + Alias + @")\s+(?:monitor|screen|display)\b", RegexOptions.IgnoreCase);
        private static readonly Regex After = new Regex(@"\b(?:monitor|screen|display)\s+(?:number\s+|no\.?\s*|#\s*)?(?<pick>[1-9][0-9]?(?:st|nd|rd|th)?)\b", RegexOptions.IgnoreCase);
        private static string Words(string value) => Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

        private static bool Multiple(string task) => Regex.IsMatch(task ?? "", @"\b(?:all|both|every)\s+(?:(?:my|the|your)\s+)?(?:monitors|screens|displays)\b|\b" + Alias + @"\s+(?:and|or|/)\s*" + Alias + @"\s+(?:monitors?|screens?|displays?)\b", RegexOptions.IgnoreCase);

        internal static bool HasHint(string task)
        {
            return Before.IsMatch(task ?? "") || After.IsMatch(task ?? "") || Multiple(task);
        }

        internal static string Resolve(HyprlandSnapshot snapshot, string screen, string task)
        {
            HyprlandOutput[] outputs = snapshot.Outputs;
            if (outputs.Length == 0) throw new InvalidOperationException("There are no connected displays to inspect.");
            string[] requested;
            {
                string text = task ?? "";
                if (Multiple(text))
                    throw Problem(outputs, "Choose one display for this request.");
                var hints = new List<string>();
                foreach (Match match in Before.Matches(text)) hints.Add(match.Groups["pick"].Value);
                foreach (Match match in After.Matches(text)) hints.Add(match.Groups["pick"].Value);
                foreach (Match match in Regex.Matches(text, @"\b(?:on|at|from|of|monitor|screen|display)\s+(?:(?:my|the|your)\s+)?(?<name>(?:DP|HDMI|DVI|eDP|VGA)(?:-[A-Za-z])?-[0-9]+)(?![\w-])", RegexOptions.IgnoreCase)) hints.Add(match.Groups["name"].Value);
                foreach (HyprlandOutput output in outputs)
                {
                    string name = Regex.Escape(output.Name);
                    // Connector names must be an actual display reference, not a substring in unrelated task data.
                    if (string.Equals(text.Trim(), output.Name, StringComparison.OrdinalIgnoreCase)
                        || Regex.IsMatch(text, @"\b(?:on|at|from|of|monitor|screen|display)\s+(?:(?:my|the|your)\s+)?" + name + @"(?![\w-])|(?<![\w-])" + name + @"\s+(?:monitor|screen|display)\b", RegexOptions.IgnoreCase)) hints.Add(output.Name);
                }
                if (!string.IsNullOrWhiteSpace(screen)) hints.Insert(0, screen.Trim());
                requested = hints.ToArray();
                if (requested.Length == 0) return null;
            }
            string selected = null;
            foreach (string request in requested)
            {
                string resolved = Pick(snapshot, request);
                if (selected != null && selected != resolved) throw Problem(outputs, "That request names more than one display.");
                selected = resolved;
            }
            return selected;
        }

        private static string Pick(HyprlandSnapshot snapshot, string requested)
        {
            HyprlandOutput[] outputs = snapshot.Outputs;
            string value = Words(requested);
            HyprlandOutput[] named = outputs.Where(o => string.Equals(o.Name, requested.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (named.Length == 1) return named[0].Name;
            value = Regex.Replace(value, @"\b(?:the|my|your|monitor|screen|display|number|no)\b|[#.]", " ");
            value = Words(value);
            named = outputs.Where(o => string.Equals(o.Name, value, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (named.Length == 1) return named[0].Name;
            string[] ordinals = { "first", "second", "third", "fourth", "fifth" };
            int ordinal = Array.IndexOf(ordinals, value) + 1;
            Match number = Regex.Match(value, @"^(?<n>[1-9][0-9]?)(?:st|nd|rd|th)?$");
            if (number.Success) ordinal = int.Parse(number.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (ordinal > 0)
            {
                HyprlandOutput[] order = outputs.OrderBy(o => o.Logical.X).ThenBy(o => o.Logical.Y).ThenBy(o => o.Name, StringComparer.Ordinal).ToArray();
                if (ordinal > order.Length) throw Problem(outputs, "That display number is not connected.");
                HyprlandOutput picked = order[ordinal - 1];
                if (outputs.Any(a => outputs.Count(o => o.Logical.X == a.Logical.X && o.Logical.Y == a.Logical.Y) != 1))
                    throw Problem(outputs, "The display order is ambiguous because outputs overlap; use a connector name.");
                return picked.Name;
            }
            if (value == "main" || value == "primary") throw Problem(outputs, "This desktop has no primary-display identity; use its position or connector name.");
            if (value == "focused" || value == "active") return One(outputs.Where(o => o.Focused), outputs, "The focused display is ambiguous.").Name;
            if (value == "portrait" || value == "vertical") return One(outputs.Where(o => o.Logical.Height > o.Logical.Width), outputs, "The portrait display is ambiguous or unavailable.").Name;
            if (value == "left" || value == "leftmost") return Extreme(outputs, o => o.Logical.X, false).Name;
            if (value == "right" || value == "rightmost") return Extreme(outputs, o => o.Logical.Right, true).Name;
            if (value == "top" || value == "upper" || value == "topmost") return Extreme(outputs, o => o.Logical.Y, false).Name;
            if (value == "bottom" || value == "lower" || value == "bottommost") return Extreme(outputs, o => o.Logical.Bottom, true).Name;
            HyprlandOutput current = null;
            if (value == "here" || value == "this" || value == "current" || value == "other" || value == "another" || value == "next" || value == "previous" || value == "up" || value == "down" || value == "left of here" || value == "right of here")
                current = One(outputs.Where(o => o.Logical.Contains(snapshot.Pointer)), outputs, "The pointer does not identify exactly one display.");
            if (value == "here" || value == "this" || value == "current") return current.Name;
            if (value == "other" || value == "another") return One(outputs.Where(o => o.Name != current.Name), outputs, "More than one other display is available; name one.").Name;
            if (value == "next" || value == "previous")
            {
                HyprlandOutput[] order = outputs.OrderBy(o => o.Logical.X).ThenBy(o => o.Logical.Y).ThenBy(o => o.Name, StringComparer.Ordinal).ToArray();
                if (order.Length < 2) throw Problem(outputs, "There is no other connected display.");
                if (order.Any(a => order.Count(z => z.Logical.X == a.Logical.X && z.Logical.Y == a.Logical.Y) != 1)) throw Problem(outputs, "The display order is ambiguous; use a connector name.");
                int position = Array.FindIndex(order, o => o.Name == current.Name);
                return order[(position + (value == "next" ? 1 : order.Length - 1)) % order.Length].Name;
            }
            if (current != null)
            {
                double cx = current.Logical.Center.X, cy = current.Logical.Center.Y;
                var candidates = outputs.Where(o => value == "left of here" ? o.Logical.Center.X < cx : value == "right of here" ? o.Logical.Center.X > cx : value == "up" ? o.Logical.Center.Y < cy : o.Logical.Center.Y > cy)
                    .Select(o => new { Output = o, Distance = Math.Pow(o.Logical.Center.X - cx, 2) + Math.Pow(o.Logical.Center.Y - cy, 2) }).ToArray();
                if (candidates.Length == 0) throw Problem(outputs, "There is no display in that direction.");
                double distance = candidates.Min(c => c.Distance);
                return One(candidates.Where(c => c.Distance == distance).Select(c => c.Output), outputs, "Two displays are equally near in that direction.").Name;
            }
            throw Problem(outputs, "I could not identify the requested display.");
        }

        private static HyprlandOutput Extreme(HyprlandOutput[] outputs, Func<HyprlandOutput, double> coordinate, bool maximum)
        {
            double edge = maximum ? outputs.Max(coordinate) : outputs.Min(coordinate);
            return One(outputs.Where(o => coordinate(o) == edge), outputs, "More than one display occupies that side; use its connector name.");
        }
        private static HyprlandOutput One(IEnumerable<HyprlandOutput> candidates, HyprlandOutput[] outputs, string error)
        {
            HyprlandOutput[] chosen = candidates.ToArray();
            if (chosen.Length != 1) throw Problem(outputs, error);
            return chosen[0];
        }
        private static InvalidOperationException Problem(HyprlandOutput[] outputs, string message)
            => new InvalidOperationException(message + " Connected displays from left to right: " + string.Join(", ", outputs.OrderBy(o => o.Logical.X).ThenBy(o => o.Logical.Y).ThenBy(o => o.Name, StringComparer.Ordinal).Select((o, i) => (i + 1) + "=" + o.Name)) + ".");
    }
}
