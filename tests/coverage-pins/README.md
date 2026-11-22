# Coverage pins

Two shipped cross behaviours that had no test, pinned so they cannot drift silently.

Run `dotnet run --project tests/coverage-pins/CoveragePins.csproj -c Release` from the repository root. It prints one `PASS` line per pin, `failures=0` and exits 0 when both hold. `--root <dir>` reads another checkout's sources, which is how the discrimination proofs run against a scratch copy.

## Settings search

`cross/MainframeSearch.cs` builds her Settings search from the controls on her Mainframe: every row it takes is found by its own label, and the hand-written `Also` calls add the words people type for it. The pin reads the rows her `Mainframe` constructor builds before `BuildMods()` and the index's rules, then checks:

- every hand-labelled row's control kind is one `Findable` takes, directly or through a base class;
- every `Also` target is a setting that really exists, so a renamed row cannot leave a key that finds nothing;
- every alias word is non-empty and listed once per target.

This is a source contract, not a running window: the tests run with no display, and constructing her Avalonia Mainframe needs one. It catches a row that appears in a kind the index ignores and an alias that outlived its setting. Deleting one alias word alone is not observable here, because the row's own label still finds it; the discrimination proof removes a row's index entry and renames an alias target instead.

## Mail moves

`app/MailTools.cs`, `app/MailBox.cs` and `app/MailWatch.cs` are linked into the cross build, and the fix for #400 lives there: a move that worked is remembered, so moving the same email again — straight after, at the same moment, or after she moved it herself while watching — is one server move and two successes that say where it is. The full cross smoke already runs that suite, because `Mods.CheckPlugins` calls `MailChecks.Run`; this pin runs the same real `MailTools`, `MailBox` and `MailWatch` against the fake IMAP and SMTP servers on loopback without a display or the smoke harness, and requires the move, move-twice and watch-and-ask results. `cross/CoveragePinsAccess.cs` is the access bridge that lets this assembly see those internals.

The plug-in layer's same-call dedup from #400 is checked by `MailChecks.CheckStarter`, which needs the loaded plug-in host; this pin leaves it to the plug-in suite.
