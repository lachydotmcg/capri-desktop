# Native font bootstrap provenance

The Linux bootstrap adapts the `RTLD_DEEPBIND` isolation used by the MIT-licensed SkiaSharp LibraryLoader, merge `482e6ee2913a08a7cad76520ccf5fbce97c7c23b` ([PR2247](https://github.com/mono/SkiaSharp/pull/2247)). The .NET6+ loader regression is tracked in [SkiaSharp3038](https://github.com/mono/SkiaSharp/issues/3038). Original inspected source and MIT license are retained here.

The pinned HarfBuzzSharp native library stays unchanged. Capri explicitly loads only its bundled copy with NOW/LOCAL/DEEPBIND before GUI font use, keeps its handle alive and fails clearly if the package is incomplete. It does not change system libraries, global loader environment, approved fonts or artwork. Native regression tests deliberately load system HarfBuzz globally in a disposable child; core dumps are disabled. A compatible baseline is recorded rather than forced to fail on every distro.
