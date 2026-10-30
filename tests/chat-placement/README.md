# Chat placement regression checks

Run `dotnet run --project tests/chat-placement/ChatPlacementChecks.csproj -c Release`.
These checks exercise output-specific coordinate/size/workspace mapping and the
single-writer queue, including hide/remap cancellation and bounded failures.
They do not open windows, access a desktop or qualify actual rendered placement.

The regression is a real Hyprland desktop whose XRandR outputs have a different
order. Direct `Window.Position` writes moved the bubble and TALK pill offscreen.
Chat windows now retain their existing Avalonia layout geometry while one native
writer places each exact owned role in compositor coordinates. Typer has its own
title (`Type to Capri`) and activates only after native placement. Art, typography,
glitch timing, gestures and input content remain in the existing `Talk.cs` code.

Actual compositor checks must confirm bubble, pill and typer beside the pet on
each output/workspace, correct size, typer keyboard focus and Escape, and no late
window move or activation after hide/close. Preserve the normal runtime and review
gate while testing the isolated candidate.

Focus regressions distinguish initial mapping deactivation from deactivation after
the typer acquires focus. Pending opening has a three-second bound and retries a
missing initial display map. Explicit hide/close or another Open cancels the old
request, and changing the compositor's input owner cancels pending activation.
Actual movement verification remains the shared native helper's responsibility.
