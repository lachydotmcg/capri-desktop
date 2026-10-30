Run the deterministic checks without a display or model:

```sh
dotnet run --project tests/computer/ComputerChecks.csproj -c Release
make -C cross/native
python3 tests/computer/native_input_checks.py cross/native/capri-input
```

The C# checks use fake desktop/input/model boundaries around the production job runner. They verify frame transforms, stale identities, permission gates, cancellation, uncertainty and canonical app argv. The native payload checks build real XKB maps without connecting to a compositor. Neither test makes a model request or controls a desktop.

For an explicitly authorized native fixture check, create a disposable opaque GTK window with an app ID starting `io.github.capri.ArchFixture` and title starting `Capri disposable input fixture`. Keep it active and completely within one output. Create a manifest with its exact `address` (`0x…`), `pid`, `app_id`, and `actions` array. An empty array captures only that fixture, letting the reviewer inspect `before.jpg` and choose image coordinates. The helper must be installed at `native/capri-input` beside the test executable.

```sh
dotnet tests/computer/bin/Release/net10.0/Capri.ComputerChecks.dll \
  --native-fixture manifest.json new-evidence-directory
```

Actions use the current JPEG's pixel coordinates, such as `{"action":"click","x":100,"y":80,"button":"left"}`, `{"action":"type","text":"fixture sentinel"}`, or `{"action":"press","keys":"TAB"}`. Each action receives a fresh cropped image and a process/session/layout check. Existing evidence directories, arbitrary apps, inactive targets and crops extending beyond the selected output are rejected. Inspect the fixture's own event/state log to verify effects; helper delivery and changed pixels alone are not application success. This harness does not qualify actual vision-model decisions, owner keyboard interference or multi-monitor physical behavior.
