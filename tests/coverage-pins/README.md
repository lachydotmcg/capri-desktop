# Coverage pins

Two shipped cross behaviours, pinned so they cannot drift silently. Her settings search had no test of its own; her mail moves already run in the full cross smoke, and this pin runs the same real code without a display or the smoke harness.

Run `dotnet run --project tests/coverage-pins/CoveragePins.csproj -c Release` from the repository root. It prints one `PASS` line per pin, `failures=0` and exits 0 when both hold. Without `--root` it reads the checkout the project lives in. `--root <dir>` reads another checkout's sources; a `--root` with no value, or one that names something that is not a directory, fails with a message naming it instead of falling back to this checkout, so a broken proof cannot look green. `tests/coverage-pins/discrimination.sh` uses it against a scratch copy.

## Settings search

`cross/MainframeSearch.cs` builds her Settings search from the controls on her Mainframe: it takes each control whose `Findable` kind and name qualify, and the hand-written `Also` calls attach the words people type to a row whose name is exactly the string they name. The pin reads the rows her `Mainframe` constructor builds before `BuildMods()` and the index's rules, then checks:

- every hand-labelled row's control kind is one `Findable` takes, directly or through a base class;
- every `Also` target is the exact label of a row the index takes. The runtime attaches aliases with `found.Name == name`, so the comparison is case- and space-sensitive: renaming `Volume` to `VOLUME` leaves the shipped `Also(index, "Volume", ...)` attached to nothing, and the pin rejects it;
- every alias word is non-empty, listed once per target, and still in the shipped list, so deleting one of her words, or adding an unlisted one, fails.

This is a source contract, not a running window: the tests run with no display, and constructing her Avalonia Mainframe needs one. It catches a row in a kind the index ignores, an alias that outlived its setting, a label whose exact text no longer meets its alias, and a shipped alias word that vanished. `discrimination.sh` proves each reject on a scratch copy: it deletes the `quiet` alias word, renames the `Volume` row to `VOLUME`, and requires a bare `--root` to exit nonzero.

## Mail moves

`app/MailTools.cs`, `app/MailBox.cs` and `app/MailWatch.cs` are linked into the cross build, and the fix for #400 lives there: a move that worked is remembered, so moving the same email again — straight after, at the same moment, or after she moved it herself while watching — is one server move and two successes that say where it is. The full cross smoke already runs that suite, because `Mods.CheckPlugins` calls `MailChecks.Run`; this pin runs the same real `MailTools`, `MailBox` and `MailWatch` against the fake IMAP and SMTP servers on loopback without a display or the smoke harness, and requires the move, move-twice and watch-and-ask results. `cross/CoveragePinsAccess.cs` is the access bridge that lets this assembly see those internals.

The plug-in layer's same-call dedup from #400 is checked by `MailChecks.CheckStarter`, which needs the loaded plug-in host; this pin leaves it to the plug-in suite.
