This production-linked Linux regression loads system HarfBuzz globally in isolated child processes, then exercises the packaged HarfBuzzSharp font API. It uses no GUI, account, microphone or owner data. The Python runner disables core dumps for every child because the uncorrected baseline may abort in native code.

```sh
dotnet build tests/font-bootstrap/FontBootstrapChecks.csproj -c Release
python3 tests/font-bootstrap/run.py tests/font-bootstrap/bin/Release/net10.0/NativeBridgeChecks.dll
```

The clean baseline, corrected loading before/after the global system library, repeated initialization and strict packaged path checks must pass. The global baseline records either a native failure or ABI compatibility on that system; it is not required to crash on every Linux image. Missing system HarfBuzz is reported as unverified interposition coverage. Native desktop startup and visual output require separate checks.

Source authority: mono/SkiaSharp [issue3038](https://github.com/mono/SkiaSharp/issues/3038) and [PR2247](https://github.com/mono/SkiaSharp/pull/2247), merge482e6ee2913a08a7cad76520ccf5fbce97c7c23b. The shipped dependencies remain pinned.
