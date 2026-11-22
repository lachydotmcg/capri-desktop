using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CapriDesktop
{
    internal static class PluginCatalogChecks
    {
        private static FieldInfo Field(string name) { return typeof(Plugins).GetField(name, BindingFlags.Static | BindingFlags.NonPublic); }
        private static object Invoke(string name, params object[] args) { return typeof(Plugins).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
        private sealed class Pages : McpLink
        {
            internal bool Repeat;
            internal string SpecialName;
            internal int Calls, PagesRead;
            internal override string Name { get { return "catalog fixture"; } }
            internal override bool Alive { get { return true; } }
            internal override string Errors { get { return ""; } }
            internal override void Start(int timeout) { }
            public override void Dispose() { }
            internal override object Request(string method, object args, int timeout)
            {
                if (method == "tools/call") { Calls++; return PluginJson.Make("content", new List<object> { PluginJson.Make("type", "text", "text", "tail called") }); }
                Check(method == "tools/list", "Unexpected catalog fixture request.");
                PagesRead++;
                string cursor = PluginJson.Str(args, "cursor");
                int page = cursor == null ? 0 : int.Parse(cursor);
                List<object> listed = new List<object>();
                for (int i = 0; i < 4; i++) listed.Add(PluginJson.Make("name", page == 0 && i == 0 && SpecialName != null ? SpecialName : "tool_" + (page * 4 + i), "description", "Fixture tool", "annotations", PluginJson.Make("readOnlyHint", true), "inputSchema", PluginJson.Make("type", "object", "properties", PluginJson.Make(), "additionalProperties", false)));
                return PluginJson.Make("tools", listed, "nextCursor", Repeat ? "1" : page < 11 ? (object)(page + 1).ToString() : null);
            }
        }

        internal static void Run(List<string> checks, string output)
        {
            string[] heardBefore = ModFloor.HeardFrom;
            Pages repeat = new Pages { Repeat = true };
            bool rejected = false;
            try { repeat.Tools(30000); } catch (McpException) { rejected = true; }
            Check(rejected && repeat.PagesRead == 2, "A repeated catalog cursor was not rejected promptly.");
            Pages pages = new Pages();
            Check(pages.Tools(30000).Count == 48 && pages.PagesRead == 12, "Discovery lost tools after page ten.");
            object oldLoaded = Field("loaded").GetValue(null), oldFolder = Field("folder").GetValue(null), oldGrants = Field("grants").GetValue(null);
            Dictionary<string, McpLink> running = (Dictionary<string, McpLink>)Field("running").GetValue(null);
            Dictionary<string, List<PluginTool>> tools = (Dictionary<string, List<PluginTool>>)Field("tools").GetValue(null);
            string id = "catalog-check", folder = Path.Combine(output, "catalog-check-cache");
            PluginInfo plugin = new PluginInfo { Id = id, Name = "Catalog fixture", Hash = "fixture", On = true, Servers = new[] { new PluginServer { Name = "server" } } };
            try
            {
                Field("loaded").SetValue(null, new List<PluginInfo> { plugin });
                Field("folder").SetValue(null, folder);
                Field("grants").SetValue(null, new Dictionary<string, string>(StringComparer.Ordinal));
                running.Add(id + "/server", pages);
                Check(Plugins.ListNow(plugin) == null, "Full fake catalog failed to publish.");
                List<PluginTool> found = Plugins.ToolsOf(id);
                Check(found.Count == 48, "The catalog retained only 32 tools.");
                PluginTool tail = found[47];
                Check(Plugins.Catalog("{\"query\":\"tool_47\"}").Contains(tail.Exposed), "Tail tool was not searchable.");
                Check(Plugins.Run(tail.Exposed, "{}").Contains("tail called") && pages.Calls == 1, "Tail tool did not route to its server.");
                List<PluginTool> cached = (List<PluginTool>)Invoke("Cached", plugin);
                Check(cached != null && cached.Count == 48 && cached[47].Exposed == tail.Exposed && PluginJson.Write(cached[47].Schema).Contains("additionalProperties"), "Full cache or schema did not round trip.");
                pages.Repeat = true;
                Check(Plugins.ListNow(plugin) != null && Plugins.ToolsOf(id).Count == 48, "A broken refresh replaced the complete catalog with partial results.");
                pages.Repeat = false;
                string cache = Path.Combine(folder, id + ".tools.json");
                File.WriteAllText(cache, "{\"hash\":\"fixture\",\"tools\":[]}");
                Check(Invoke("Cached", plugin) == null, "A legacy truncated cache was accepted.");
                Dictionary<string, string> grants = (Dictionary<string, string>)Field("grants").GetValue(null);
                grants[id + " " + tail.Permission] = Plugins.Off;
                string disabledCatalog = Plugins.Catalog("{\"query\":\"tool_47\"}"), disabledCall = Plugins.Run(tail.Exposed, "{}");
                Check(!disabledCatalog.Contains(tail.Exposed) && disabledCall.Contains("turned it off") && pages.Calls == 1, "Off tool was discoverable or ran: " + disabledCall + "; calls=" + pages.Calls);
                grants.Clear();
                PluginInfo multiple = new PluginInfo { Id = "catalog-permissions", Name = "Multiple", On = true, Servers = new[] { new PluginServer { Name = "Docs Server" }, new PluginServer { Name = "Other Server" } } };
                Dictionary<string, object> shared = PluginJson.Make("name", "search", "inputSchema", PluginJson.Make("type", "object"));
                PluginTool docsSearch = (PluginTool)Invoke("Made", multiple, "Docs Server", shared, new List<PluginTool>());
                PluginTool otherSearch = (PluginTool)Invoke("Made", multiple, "Other Server", shared, new List<PluginTool>());
                ((List<PluginInfo>)Field("loaded").GetValue(null)).Add(multiple);
                PluginTool docsInspect = (PluginTool)Invoke("Made", multiple, "Docs Server", PluginJson.Make("name", "inspect", "inputSchema", PluginJson.Make("type", "object")), new List<PluginTool>());
                PluginTool otherInspect = (PluginTool)Invoke("Made", multiple, "Other Server", PluginJson.Make("name", "inspect", "inputSchema", PluginJson.Make("type", "object")), new List<PluginTool>());
                PluginTool onlyAlways = (PluginTool)Invoke("Made", multiple, "Docs Server", PluginJson.Make("name", "only-always", "inputSchema", PluginJson.Make("type", "object")), new List<PluginTool>());
                tools.Add(multiple.Id, new List<PluginTool> { docsSearch, otherSearch, docsInspect, otherInspect, onlyAlways });
                List<Dictionary<string, object>> unsafeCatalog = new Pages { SpecialName = "read docs\nnext" }.Tools(30000);
                Check(PluginJson.Str(unsafeCatalog[0], "name") == "read docs\nnext", "The unsafe-name persistence fixture did not pass real catalog discovery.");
                PluginTool spaced = (PluginTool)Invoke("Made", plugin, "server", unsafeCatalog[0], new List<PluginTool>());
                Check(docsSearch.Permission != otherSearch.Permission && docsSearch.Permission.IndexOf(' ') < 0 && spaced.Permission.IndexOf(' ') < 0 && spaced.Permission.IndexOf('\n') < 0 && tail.Permission == tail.Tool, "Permission keys were not safe, distinct or legacy compatible.");
                PluginTool reserved = (PluginTool)Invoke("Made", plugin, "server", PluginJson.Make("name", "tool:reserved", "inputSchema", PluginJson.Make("type", "object")), new List<PluginTool>());
                found.Add(reserved);
                tools[id] = found;
                string upgradedGrants = Path.Combine(folder, "grants-v2.json");
                if (File.Exists(upgradedGrants)) File.Delete(upgradedGrants);
                File.WriteAllText(Path.Combine(folder, "grants.txt"), id + " tool:reserved off\n" + id + " " + tail.Tool + " ask\n" + multiple.Id + " " + docsSearch.Permission + " always\n" + multiple.Id + " search off\n" + multiple.Id + " inspect ask\n" + multiple.Id + " only-always always\ncatalog-later tool:later ask\n");
                Field("grants").SetValue(null, null);
                Check(Plugins.GrantOf(id, reserved.Permission) == Plugins.Off && Plugins.GrantOf(id, tail.Permission) == Plugins.Ask && Plugins.GrantOf(multiple.Id, docsSearch.Permission) == Plugins.Off, "Legacy raw grants were lost or a legacy Always authorized a generated identity.");
                Plugins.Grant(plugin.Id, tail.Permission, Plugins.Ask);
                Check(File.Exists(upgradedGrants) && File.ReadAllText(Path.Combine(folder, "grants.txt")).Contains("tool:reserved off"), "The grant upgrade overwrote the original permissions file.");
                Field("grants").SetValue(null, null);
                Check(Plugins.GrantOf(id, reserved.Permission) == Plugins.Off && Plugins.GrantOf(multiple.Id, docsSearch.Permission) == Plugins.Off, "Reserved legacy restrictions or generated-key isolation did not survive the version upgrade.");
                PluginInfo later = new PluginInfo { Id = "catalog-later", Servers = new[] { new PluginServer { Name = "Later Server" } } };
                PluginTool laterTool = (PluginTool)Invoke("Made", later, "Later Server", PluginJson.Make("name", "tool:later", "inputSchema", PluginJson.Make("type", "object")), new List<PluginTool>());
                ((List<PluginInfo>)Field("loaded").GetValue(null)).Add(later);
                tools.Add(later.Id, new List<PluginTool> { laterTool });
                Check(Plugins.GrantOf(later.Id, laterTool.Permission) == Plugins.Ask, "An unloaded legacy restriction was discarded before its plugin loaded.");
                tools.Remove(later.Id);
                Plugins.Forget(later.Id);
                Check(!File.ReadAllText(Path.Combine(folder, "grants.txt")).Contains("catalog-later"), "Removing a plugin left its old-version grants behind.");
                Check(Plugins.GrantOf(multiple.Id, otherSearch.Permission) == Plugins.Off && Plugins.GrantOf(multiple.Id, docsInspect.Permission) == Plugins.Ask && Plugins.GrantOf(multiple.Id, otherInspect.Permission) == Plugins.Ask && Plugins.GrantOf(multiple.Id, onlyAlways.Permission) == Plugins.Usual, "Loaded server tools lost legacy Off/Ask restrictions or inherited raw Always.");
                Check(Plugins.Run(docsSearch.Exposed, "{}").Contains("turned it off") && Plugins.Granted(docsInspect.Exposed) == 1, "Inherited legacy restrictions did not govern the actual loaded tools.");
                Plugins.Grant(multiple.Id, docsSearch.Permission, Plugins.Usual);
                Plugins.Grant(multiple.Id, docsInspect.Permission, Plugins.Usual);
                Field("grants").SetValue(null, null);
                Check(Plugins.GrantOf(multiple.Id, docsSearch.Permission) == Plugins.Usual && Plugins.GrantOf(multiple.Id, docsInspect.Permission) == Plugins.Usual && Plugins.GrantOf(multiple.Id, otherSearch.Permission) == Plugins.Off && Plugins.GrantOf(multiple.Id, otherInspect.Permission) == Plugins.Ask && Plugins.GrantOf(multiple.Id, onlyAlways.Permission) == Plugins.Usual, "Scoped Usual did not survive reload or erased another server's inherited restriction.");
#if !CROSS
                Check(Mainframe.NextPluginGrant(otherInspect) == Plugins.Off && Plugins.GrantOf(multiple.Id, otherInspect.Permission) == Plugins.Off && Plugins.GrantOf(multiple.Id, docsInspect.Permission) == Plugins.Usual, "The tool-sheet action changed a raw name instead of the selected server permission.");
                Plugins.Grant(multiple.Id, otherInspect.Permission, Plugins.Ask);
#endif
                Plugins.Grant(multiple.Id, "search", Plugins.Always);
                Check(Plugins.GrantOf(multiple.Id, docsSearch.Permission) == Plugins.Usual && Plugins.GrantOf(multiple.Id, otherSearch.Permission) == Plugins.Off, "A raw grant migrated across servers or removed an inherited restriction.");
                Plugins.Grant(multiple.Id, docsSearch.Permission, Plugins.Off);
                Plugins.Grant(multiple.Id, otherSearch.Permission, Plugins.Ask);
                Plugins.Grant(plugin.Id, spaced.Permission, Plugins.Off);
                Plugins.Grant(plugin.Id, tail.Permission, Plugins.Ask);
                Field("grants").SetValue(null, null);
                Check(Plugins.GrantOf(multiple.Id, docsSearch.Permission) == Plugins.Off && Plugins.GrantOf(multiple.Id, otherSearch.Permission) == Plugins.Ask && Plugins.GrantOf(plugin.Id, spaced.Permission) == Plugins.Off && Plugins.GrantOf(plugin.Id, tail.Permission) == Plugins.Ask, "Server-specific or unsafe-name grants did not survive persistence reload.");
                Check(Plugins.Run(docsSearch.Exposed, "{}").Contains("turned it off") && Plugins.Granted(otherSearch.Exposed) == 1, "Reloaded server permissions did not govern the loaded tools.");
                grants = (Dictionary<string, string>)Field("grants").GetValue(null);
                grants.Clear();
                string offer = PluginJson.Write(Plugins.CatalogOffer());
                Check(offer.Contains(Plugins.CatalogName) && !offer.Contains(tail.Exposed), "Plugin schemas leaked into the compact offer.");
                Dictionary<string, object> raw = PluginJson.Make("name", "same", "inputSchema", PluginJson.Make("type", "object"));
                PluginTool a = (PluginTool)Invoke("Made", plugin, "a-b", raw, new List<PluginTool>());
                PluginTool b = (PluginTool)Invoke("Made", plugin, "a_b", raw, new List<PluginTool>());
                Check(a.Exposed != b.Exposed && a.Exposed == ((PluginTool)Invoke("Made", plugin, "a-b", raw, new List<PluginTool> { b })).Exposed, "Canonical identity collided or depended on listing order.");
                PluginInfo twin = new PluginInfo { Id = "catalog_check", Name = "Twin", On = true, Servers = plugin.Servers };
                PluginTool collision = (PluginTool)Invoke("Made", twin, "server", PluginJson.Make("name", "tool_47", "inputSchema", PluginJson.Make("type", "object")), new List<PluginTool>());
                ((List<PluginInfo>)Field("loaded").GetValue(null)).Add(twin);
                tools.Add(twin.Id, new List<PluginTool> { collision });
                Check(collision.Exposed != tail.Exposed && !Plugins.Owns(tail.Alias) && Plugins.Owns(tail.Exposed), "A normalized cross-plugin alias silently chose one tool.");
                string securityBefore = Security.Saved;
                bool wakeBefore = Security.On(Security.Wake);
                try
                {
                    Security.Pick(Security.Trusted);
                    grants[twin.Id + " " + collision.Permission] = Plugins.Always;
                    ToolArguments write = new ToolArguments { Name = collision.Exposed, Text = "{}" };
                    ModFloor.Fresh();
                    Check(Security.Gate("mod", write) == null, "The catalog guard fixture's write was already held before discovery.");
                    Plugins.Catalog("{\"plugin\":\"" + id + "\",\"query\":\"tool_47\"}");
                    Check(ModFloor.HeardFrom.Length == 1 && ModFloor.HeardFrom[0] == "plugin:" + id, "Discovery marked plugins whose definitions were not returned.");
                    Check(Security.IsHold(Security.Gate("mod", write)) && Security.Gate("mod", new ToolArguments { Name = tail.Exposed }) == null, "Discovery bypassed cross-plugin write approval or held a read-only tool.");
                    Security.Drop();
                    ModFloor.Fresh();
                    Plugins.Catalog("{\"plugin\":\"" + id + "\",\"offset\":9999}");
                    Check(ModFloor.HeardFrom.Length == 0, "An empty discovery page marked external definitions as read.");
                }
                finally
                {
                    Security.Drop(); Security.Load(securityBefore, wakeBefore);
                    Action changed = Security.Changed; if (changed != null) changed();
                }
                tools.Remove(twin.Id);
                plugin.On = false;
                Check(!Plugins.Owns(tail.Exposed) && !Plugins.Catalog("{}").Contains(tail.Exposed), "Disabled plugin remained callable or discoverable.");
                checks.Add("PASS plugin catalogs: 12 pages and 48 tools, tail execution/cache/schema, repeated cursor rejection, stable server identities and persisted permissions, compact offers, discovery source approval guards and disabled-tool enforcement");
            }
            finally
            {
                running.Remove(id + "/server"); tools.Remove(id); tools.Remove("catalog_check"); tools.Remove("catalog-later"); tools.Remove("catalog-permissions");
                ((Dictionary<string, string>)Field("troubles").GetValue(null)).Remove(id);
                Field("loaded").SetValue(null, oldLoaded); Field("folder").SetValue(null, oldFolder); Field("grants").SetValue(null, oldGrants);
                ModFloor.Fresh(); foreach (string source in heardBefore) ModFloor.Heard(source);
            }
        }
    }
}
