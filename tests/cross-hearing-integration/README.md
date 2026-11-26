# Cross hearing and diagnostics

Loads a built `Capri.dll` by reflection and checks the hearing line under VOICE on a Mac or Linux build: the state machine, her recorder's framing, the UI, and the TALK and mic-test lifecycles against fixture PCM. It is an independent verifier, so it takes the app assembly instead of linking it.

```
dotnet build cross/Capri.Cross.csproj -c Release -o out/cross
dotnet run --project tests/cross-hearing-integration/ -c Release -- \
    "$PWD/out/cross/Capri.dll" "$(mktemp -d)" "$PWD/tests/cross-hearing-integration/fake" core
```

The fourth argument is the mode: `core` for the state machine, thresholds and recorder framing; `candidate` and `candidate-lifecycle` for the behaviours being added, the second through the real `Ears.Listen` path; `baseline` and `baseline-lifecycle` for the same checks against the code before them.

`fake/parec` and `fake/sherpa-onnx-offline` stand in for her recorder and offline ears. The probe puts that folder first on `PATH` and drives them with `E7_MODE` and `E7_PIDS`, so it never reads or writes a real microphone or a real Capri profile. The UI modes need a display: run them under `xvfb-run -a` on a headless Linux box, and point `HOME` at a scratch folder with a `.config` inside it, or the app keeps its `Capri` folder beside the checkout instead.
