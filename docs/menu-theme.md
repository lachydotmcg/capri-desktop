# Native context-menu palette

Avalonia11.3.22’s [MenuItem theme](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Themes.Fluent/Controls/MenuItem.xaml) assigns foregrounds and selected/pressed/disabled presenters through DynamicResources. Submenu borders use their own presenter resources. ContextMenu.Foreground alone cannot override those values. With Capri’s White look and the existing requested Dark framework theme, rows therefore rendered white on a white background while their text and layout were present.

MainframeTheme.ConfigureMenu scopes those exact resources to the existing ContextMenu and uses the existing mutable look brushes. It covers normal/hover/pressed/disabled labels, accelerator text, chevrons, submenu sheets and separators, including live look changes. The [ContextMenu template](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Themes.Fluent/Controls/ContextMenu.xaml) and [Separator template](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Themes.Fluent/Controls/Separator.xaml) retain their original geometry. No global theme, native override-redirect/input policy, renderer fallback or repaint timer changes.

Build the retained tests/native-ui fixture and run it only with a disposable profile. Open the real menu, then issue menu-diagnostic and menu-palette-checks. The regression checks effective styled-control ink across all four looks and live changes; requireSubmenu:true additionally requires an actually open submenu. The incoming White-look implementation fails before resource assertions because enabled rows use white ink. Actual native screenshots, repeated input and platform checks remain separate acceptance evidence.

The optional menu-palette-diagnostic command is a test-only controlled A/B resource override; it is never called by the regression or shipped application. menu-render produces an offscreen diagnostic image, not proof of native presentation. Bounded diagnostic traces omit accounts, history and credentials.

## Native submenu stacking

The root context menu already receives Capri's process-specific native menu identity before mapping. Submenu hosts also need that identity: leaving them as unclassified override-redirect popups bypassed the scoped Hyprland popup rules and allowed Capri to overlap their final rows at screen edges. NativeMenuStack applies the existing NativeX11 configuration to each PART_Popup child at visual attachment. In the pinned [Popup implementation](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Controls/Primitives/Popup.cs), SetChild attaches before Show; Opened and PopupHostChanged occur after Show and are too late for map-time rules. Topmost, geometry, input handling and process-rule scope are retained.

After actual Size, Move and Help opens, the fixture's menu-diagnostic trace and a compositor client capture can be checked offline:

```sh
python3 tests/native-ui/verify-submenu-native.py /absolute/disposable/menu-diagnostic.jsonl --capture /absolute/public/submenu.json
```

The capture JSON must contain the `clients` array from the public native QA session. The checker requires managed mapped submenu/render windows and process-classified, pinned/floating root and child hosts. The previous implementation fails on override-redirect classification. This is native metadata evidence; inspect actual settled submenu pixels above Capri and neighboring windows, input/dismissal, edges and topmost modes separately.

The delayed cold-start greeting also skips an already open context menu, alongside its existing conversation/quiet guards. It is not rescheduled: an unsolicited greeting must not cover the menu the user has just opened. Ordinary startup greeting behavior remains separate from that interaction.

The offline checker binds every trace record and capture to the same positive process ID and exact nonce-bearing menu class. Run its identity/rejection regressions with `python3 tests/native-ui/test_verify_submenu_native.py`. These checks do not turn metadata into pixel or human acceptance evidence.
