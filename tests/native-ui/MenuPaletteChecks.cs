using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using CapriDesktop;

// Real styled-control checks. Run only in the disposable native UI fixture with
// an already-open menu. They never call ConfigureMenu or the diagnostic fix,
// so the old White-look menu fails from its effective foreground value.
internal static class MenuPaletteChecks
{
    internal static object Run(ContextMenu menu, bool requireSubmenu)
    {
        if (!menu.IsOpen) throw new InvalidOperationException("Open the native menu before palette checks.");
        var rows = menu.Items.OfType<MenuItem>().Where(i => i.IsVisible && i.IsEnabled).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("No visible enabled menu rows.");
        var row = rows[0];
        var presenter = row.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_HeaderPresenter");
        var submenus = menu.Items.OfType<MenuItem>().Where(i => i.IsSubMenuOpen)
            .SelectMany(i => i.GetVisualDescendants().OfType<Popup>())
            .Select(p => p.Child).OfType<Border>().ToArray();
        if (requireSubmenu && submenus.Length == 0) throw new InvalidOperationException("Open Size, Move or Help before requiring submenu palette checks.");
        int originalLook = (int)typeof(MainframeTheme).GetField("shade", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        bool originalSelected = row.IsSelected, originalEnabled = row.IsEnabled;
        var checks = new List<string>();
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException("Menu palette regression: " + description);
            checks.Add(description);
        }
        bool Same(IBrush a, IBrush b) => a?.ToString() == b?.ToString();
        try
        {
            // Keep one native menu and its controls alive: this also catches stale
            // palette snapshots when switching away from White and back to it.
            foreach (int look in new[] { 2, 0, 1, 3, 2 })
            {
                MainframeTheme.UseLook(look);
                string name = MainframeTheme.LookNames[look];
                row.IsEnabled = true;
                row.IsSelected = false;
                foreach (var visible in rows)
                    Check(Same(visible.Foreground, MainframeTheme.White), name + " row ink: " + visible.Header);
                Check(Same(presenter.Foreground, MainframeTheme.White), name + " normal header ink");
                row.IsSelected = true;
                Check(Same(presenter.Foreground, MainframeTheme.White), name + " selected header ink");
                row.IsEnabled = false;
                Check(Same(presenter.Foreground, MainframeTheme.Faint), name + " disabled header ink");
                row.IsEnabled = true;
                row.IsSelected = false;
                foreach (var submenu in submenus)
                {
                    Check(Same(submenu.Background, MainframeTheme.Ground), name + " open submenu background");
                    Check(Same(submenu.BorderBrush, MainframeTheme.Brim), name + " open submenu border");
                }
                var expected = new Dictionary<string, IBrush>
                {
                    ["MenuFlyoutPresenterBackground"] = MainframeTheme.Ground,
                    ["MenuFlyoutPresenterBorderBrush"] = MainframeTheme.Brim,
                    ["MenuFlyoutItemBackground"] = Brushes.Transparent,
                    ["MenuFlyoutItemBackgroundPointerOver"] = MainframeTheme.Hover,
                    ["MenuFlyoutItemBackgroundPressed"] = MainframeTheme.Pressed,
                    ["MenuFlyoutItemBackgroundDisabled"] = Brushes.Transparent,
                    ["MenuFlyoutItemForeground"] = MainframeTheme.White,
                    ["MenuFlyoutItemForegroundPointerOver"] = MainframeTheme.White,
                    ["MenuFlyoutItemForegroundPressed"] = MainframeTheme.White,
                    ["MenuFlyoutItemForegroundDisabled"] = MainframeTheme.Faint,
                    ["MenuFlyoutItemKeyboardAcceleratorTextForeground"] = MainframeTheme.Faint,
                    ["MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver"] = MainframeTheme.White,
                    ["MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed"] = MainframeTheme.White,
                    ["MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled"] = MainframeTheme.Faint,
                    ["MenuFlyoutSubItemChevron"] = MainframeTheme.White,
                    ["MenuFlyoutSubItemChevronPointerOver"] = MainframeTheme.White,
                    ["MenuFlyoutSubItemChevronPressed"] = MainframeTheme.White,
                    ["MenuFlyoutSubItemChevronDisabled"] = MainframeTheme.Faint,
                    ["MenuFlyoutSubItemChevronSubMenuOpened"] = MainframeTheme.Cyan,
                    ["SystemControlForegroundBaseMediumLowBrush"] = MainframeTheme.Rule
                };
                foreach (var entry in expected)
                    Check(menu.Resources.ContainsKey(entry.Key) && ReferenceEquals(menu.Resources[entry.Key], entry.Value), name + " live brush: " + entry.Key);
            }
        }
        finally
        {
            row.IsEnabled = originalEnabled;
            row.IsSelected = originalSelected;
            MainframeTheme.UseLook(originalLook);
        }
        return new { status = "PASS", checks, submenuTemplates = submenus.Length, scope = "effective styled controls plus pinned Fluent state resources; actual pixel/input acceptance is separate" };
    }
}
