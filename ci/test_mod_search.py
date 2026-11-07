import os
import re
import sys

root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), ".."))


def read(*parts):
    with open(os.path.join(root, *parts), encoding="utf-8") as f:
        return f.read()


mods = read("app", "MainframeMods.cs")
window = read("app", "MainframeModsWindow.cs")

fill = re.search(r"private void FillMods\(\)\s*\{(.*?)\n        \}", mods, re.S)
if not fill:
    raise AssertionError("FillMods was not found in app/MainframeMods.cs")
fill = fill.group(1)

if "ShownMods(capri.ModList.All, modSearch.Box.Text)" not in fill:
    raise AssertionError("The installed list does not filter by the search box.")
if "modSearch.Box.TextChanged" not in mods or "modFirst = 0; FillMods();" not in mods:
    raise AssertionError("Typing in the installed search does not restart paging at page 1.")
if "modNoMatch" not in mods or "No mods match." not in mods:
    raise AssertionError("The installed list has no 'No mods match' message.")

for name, source in (("app/MainframeMods.cs", mods), ("app/MainframeModsWindow.cs", window)):
    if re.search(r"ModList\.All\[\s*modFirst", source):
        raise AssertionError(name + " still picks a mod by its position in ModList.All, not the filtered list.")

guard = re.search(r"private Mod ShownMod\(int row\)\s*\{(.*?)\n        \}", mods, re.S)
if not guard or "modShown[at]" not in guard.group(1) or "ModList.All.Contains(modShown[at])" not in guard.group(1):
    raise AssertionError("ShownMod does not check the row's mod is still installed.")

for method in ("SwitchMod", "RemoveMod"):
    body = re.search(method + r"\(int row\)\s*\{(.{0,200})", mods, re.S)
    if not body or "ShownMod(row)" not in body.group(1):
        raise AssertionError(method + " does not act on the filtered list.")

body = re.search(r"PublishMod\(int row\)\s*\{(.{0,200})", window, re.S)
if not body or "ShownMod(row)" not in body.group(1):
    raise AssertionError("PublishMod does not act on the filtered list.")

print("PASS installed mods search filters, pages and acts on the shown mods")
