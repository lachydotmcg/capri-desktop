# Native surface lifecycle checks

Run `dotnet run --project tests/native-surface/NativeSurface.Tests.csproj`.

These checks compile the production surface scheduler and passive-window lifecycle against controlled window, compositor and dispatcher boundaries. They exercise held/failed transport requests, release fences, stale layouts, locked/hidden windows, worker retirement and rapid Show/Hide/Show. They never connect to X11/Hyprland, inspect a real desktop or inject input. The boundary stubs do not prove native API behavior; `tests/native-ui` supplies the separate disposable native fixture.

A compositor request already accepted cannot be unsent. Cancellation invalidates pending work; release waits for accepted work to settle before continuing physics. Deliberately delayed replies must not be reported as a successfully applied final position after a layout/lock invalidation.
