using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CapriDesktop
{
    // Avalonia counterpart of the released glass editor. The editor is a regular,
    // focusable window; passive pet/screenshot/input policies are not reused here.
    internal sealed class NativeFileEditor : Window
    {
        internal static readonly FontFamily TitleFace = new FontFamily("avares://Capri/Assets#Capri Mainframe");
        internal event Action Saved;
        internal bool Motion = true;
        private readonly NativeEditorDocument[] docs;
        private readonly NativeEditorMotion animation = new NativeEditorMotion();
        private readonly DispatcherTimer ticks = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / 60) };
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Canvas root = new Canvas { Background = Brushes.Transparent };
        private readonly Canvas page = new Canvas { Background = Brushes.Transparent };
        private readonly ArtLayer shadow = new ArtLayer { Shadow = true, IsHitTestVisible = false, Opacity = .6, Effect = new BlurEffect { Radius = 5 } };
        private readonly ArtLayer art = new ArtLayer { IsHitTestVisible = false };
        private readonly TextBox words;
        private readonly TextBlock heading, status;
        private readonly EditorButton[] tabs;
        private readonly EditorButton save, reload, keep, close;
        private int current, displayed = -1;
        private bool activationRefreshQueued;
        private bool loading, permitted, started, guarded;
        private readonly bool[] dirty = new bool[2];
        private Task pending = Task.CompletedTask;
        private int lifetime;
        internal bool Busy { get; private set; }
        internal Task WhenIdle => pending;
        private double scale = 1.6, previous;
        private Action after;
        internal bool Over { get; private set; }
        internal bool Dirty => dirty.Any(value => value);
        internal bool Guarding => guarded;
        internal bool Resting => animation.Stage == NativeEditorMotion.Phase.Open;
        internal double Frame => animation.Frame;
        internal string FileShown => docs[current].Label;
        internal string Draft { get => words.Text ?? ""; set { if (Busy) throw new InvalidOperationException("The editor is reading or saving. Try again when it is ready."); words.Text = value; } }
        internal string Status => status.Text;
        internal TextBox TextArea => words;
        internal Rect PageBox => NativeEditorArt.Page(scale);
        internal WindowTransparencyLevel EffectiveTransparency => ActualTransparencyLevel;
        internal string BlurStatus => OperatingSystem.IsLinux() && HyprlandDesktop.Current != null
            ? "transparent fallback; compositor panel-region blur unavailable"
            : ActualTransparencyLevel == WindowTransparencyLevel.Blur ? "backend blur; panel-region clipping unverified" : "transparent fallback; panel-region blur unavailable";

        internal NativeFileEditor(NativeEditorDocument[] documents = null)
        {
            docs = documents ?? new[] {
                new NativeEditorDocument("personality.md", Brain.OpenPersonality),
                new NativeEditorDocument("me.md", () => Brain.OpenNote(false))
            };
            if (docs.Length != 2) throw new ArgumentException("The editor needs its two independent notes.", nameof(documents));
            Title = "Capri file editor";
            NativeSurface.Register(this, NativeSurfaceRole.FileEditor);
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent };
            ShowInTaskbar = true; CanResize = false; CanMaximize = false; CanMinimize = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            MinWidth = MinHeight = 0;
            words = new TextBox { AcceptsReturn = true, AcceptsTab = true, NewLine = "\n", TextWrapping = TextWrapping.Wrap,
                FontFamily = Look.Mono, FontSize = 17, LineHeight = 23, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = new SolidColorBrush(NativeEditorArt.Salmon), CaretBrush = new SolidColorBrush(NativeEditorArt.Salmon),
                SelectionBrush = new SolidColorBrush(Color.FromArgb(170, 139, 43, 44)), SelectionForegroundBrush = Brushes.White,
                VerticalContentAlignment = VerticalAlignment.Top, MinWidth = 0, MinHeight = 0 };
            foreach (string state in new[] { "", "PointerOver", "Focused", "Disabled" })
            {
                words.Resources["TextControlBackground" + state] = Brushes.Transparent;
                words.Resources["TextControlForeground" + state] = new SolidColorBrush(NativeEditorArt.Salmon);
                words.Resources["TextControlBorderBrush" + state] = Brushes.Transparent;
            }
            ScrollViewer.SetVerticalScrollBarVisibility(words, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(words, ScrollBarVisibility.Disabled);
            // TextChanging is synchronous in pinned Avalonia11.3.22; TextChanged is deferred past a tab swap.
            words.TextChanging += (_, _) => { if (!loading && !Busy) { docs[current].Edit(words.Text); Mark(); } };
            heading = new TextBlock { FontFamily = TitleFace, FontWeight = FontWeight.Bold, FontSize = 30,
                Foreground = new SolidColorBrush(NativeEditorArt.Salmon), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            status = new TextBlock { FontFamily = Look.Mono, FontSize = 13, Foreground = new SolidColorBrush(NativeEditorArt.Salmon),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            tabs = new[] { new EditorButton(), new EditorButton() };
            for (int i = 0; i < tabs.Length; i++) { int index = i; tabs[i].Click += (_, _) => Turn(index); }
            close = new EditorButton { Cross = true }; close.Click += (_, _) => Ask();
            save = new EditorButton { Text = "Save", Filled = true }; save.Click += async (_, _) => { if (guarded) await GuardSave(); else await Save(); };
            reload = new EditorButton { Text = "Reload" }; reload.Click += async (_, _) => { if (guarded) await GuardDiscard(); else await Reload(); };
            keep = new EditorButton { Text = "Keep editing", IsVisible = false }; keep.Click += (_, _) => GuardKeep();
            ToolTip.SetTip(reload, "Reload the file from disk, discarding this tab's edits.");
            AutomationProperties.SetName(close, "Close the file editor");
            page.Children.AddRange(new Control[] { heading, tabs[0], tabs[1], words, close, status, keep, reload, save });
            root.Children.Add(shadow); root.Children.Add(art); root.Children.Add(page); Content = root;
            AddHandler(KeyDownEvent, OnEditorKey, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, OnEditorPointer, RoutingStrategies.Tunnel);
            Opened += async (_, _) => { started = true; Fit(); if (!Motion) animation.FinishOpen(); StartClock(); if (!Motion) ticks.Stop(); await Take(current, false); };
            Activated += (_, _) => QueueActivationRefresh();
            Closing += OnClosing;
            Closed += (_, _) => { ticks.Stop(); Over = true; lifetime++; };
            ticks.Tick += (_, _) => Tick();
        }

        internal void Open(int which)
        {
            if (which < 0 || which >= docs.Length) throw new ArgumentOutOfRangeException(nameof(which));
            if (Over) throw new InvalidOperationException("Create a new editor after it has closed.");
            if (animation.Leaving) { after = null; animation.Open(); StartClock(); }
            if (!IsVisible) { current = which; Show(); } else { Turn(which); Activate(); }
        }

        private void Fit()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            PixelRect area = screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 900);
            double dpi = screen?.Scaling ?? 1;
            scale = Math.Min(1.6, Math.Min(area.Width / dpi / NativeEditorArt.Wide, area.Height / dpi / NativeEditorArt.High));
            scale = Math.Max(.5, scale);
            Width = NativeEditorArt.Wide * scale; Height = NativeEditorArt.High * scale;
            Position = new PixelPoint(area.X + Math.Max(0, (int)((area.Width - Width * dpi) / 2 - area.Width / 16)),
                area.Y + Math.Max(0, (int)((area.Height - Height * dpi) / 2 - area.Height / 16)));
            shadow.Width = art.Width = Width; shadow.Height = art.Height = Height; shadow.Scale = art.Scale = scale;
            Rect box = PageBox; Canvas.SetLeft(page, box.X); Canvas.SetTop(page, box.Y); page.Width = box.Width; page.Height = box.Height;
            double pad = 18, gap = 8, row = 52, foot = 48;
            Put(close, box.Width - pad - 28, 12, 28, 28);
            double right = box.Width - pad - 28 - 12;
            for (int i = tabs.Length - 1; i >= 0; i--)
            {
                double wide = TextWidth(docs[i].Label + " *", 14) + 24;
                right -= wide; Put(tabs[i], right, 12, wide, 28); right -= gap;
            }
            Put(heading, pad, 4, Math.Max(40, right - pad), row - 6);
            double body = Math.Max(23, Math.Floor((box.Height - row - foot) / 23) * 23);
            Put(words, pad, row, Math.Max(60, box.Width - pad * 2), body);
            PlaceFoot();
        }
        private void PlaceFoot()
        {
            double at = page.Width - 18, top = page.Height - 48 + 9;
            foreach (EditorButton button in new[] { save, reload, keep })
            {
                if (!button.IsVisible) continue;
                double width = TextWidth(button.Text, 14) + 26; at -= width; Put(button, at, top, width, 30); at -= 8;
            }
            Put(status, 18, top, Math.Max(40, at - 18), 30);
        }
        private static void Put(Control control, double x, double y, double width, double height)
        { Canvas.SetLeft(control, x); Canvas.SetTop(control, y); control.Width = Math.Max(0, width); control.Height = Math.Max(0, height); }
        private static double TextWidth(string text, double size) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(TitleFace, FontStyle.Normal, FontWeight.Bold), size, Brushes.White).Width;

        private void StartClock() { previous = 0; clock.Restart(); ticks.Start(); PaintAnimation(); }
        private void Tick()
        {
            double now = clock.Elapsed.TotalSeconds; animation.Advance(now - previous); previous = now;
            PaintAnimation();
            if (animation.Stage == NativeEditorMotion.Phase.Open) ticks.Stop();
            if (animation.Stage == NativeEditorMotion.Phase.Gone)
            {
                ticks.Stop(); Action next = after; after = null; permitted = true; Close(); next?.Invoke();
            }
        }
        private void PaintAnimation()
        {
            shadow.Frame = art.Frame = animation.Frame; shadow.InvalidateVisual(); art.InvalidateVisual();
            page.IsHitTestVisible = !Busy && !animation.Leaving;
            page.Opacity = animation.Stage == NativeEditorMotion.Phase.Untyping ? animation.Words : NativeEditorArt.Grown(animation.Frame, 35, 42);
            words.Opacity = animation.Words;
            string name = docs[current].Label;
            int shown = animation.Stage == NativeEditorMotion.Phase.Untyping ? (int)Math.Ceiling(name.Length - animation.Untyped * 1.5) : (int)Math.Floor((animation.Frame - 40) * 1.5) + 1;
            heading.Text = name.Substring(0, Math.Clamp(shown, 0, name.Length));
        }
        internal void FinishOpen()
        { if (!animation.Leaving) { animation.FinishOpen(); ticks.Stop(); PaintAnimation(); } }
        private async void OnEditorKey(object sender, KeyEventArgs e)
        {
            if (Busy) { e.Handled = true; return; }
            if (e.Key == Key.S && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            { FinishOpen(); e.Handled = true; if (guarded) await GuardSave(); else await Save(); }
            else if (e.Key == Key.Escape) { if (guarded) GuardKeep(); else Ask(); e.Handled = true; }
            else FinishOpen();
        }
        private void OnEditorPointer(object sender, PointerPressedEventArgs e)
        {
            FinishOpen();
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual visual && visual.GetVisualAncestors().Prepend(visual).Any(v => v is TextBox || v is Button || v is ScrollBar)) return;
            BeginMoveDrag(e);
        }
        internal void Turn(int which)
        {
            if (which < 0 || which >= docs.Length) throw new ArgumentOutOfRangeException(nameof(which));
            _ = Take(which);
        }
        private void KeepCurrent() { if (!loading) docs[current].Edit(words.Text); }

        // All document I/O is serialized here. The UI snapshots its text before the
        // worker owns the documents, then disables every route that could mutate them.
        // UI state is copied only after the worker returns; no worker touches controls.
        private Task<bool> Operate(Action prepare, Func<bool> work, Action<bool> complete, string message)
        {
            Task previous = pending;
            Task<bool> operation = Run(previous, prepare, work, complete, message);
            pending = operation;
            return operation;
        }
        private async Task<bool> Run(Task previous, Action prepare, Func<bool> work, Action<bool> complete, string message)
        {
            await previous;
            if (Over) return false;
            int generation = lifetime;
            // Disabling a focused TextBox releases keyboard focus. Keep its UI
            // selection separate from the worker-owned document so Ctrl+S returns
            // to the exact editing position without stealing another window's focus.
            bool hadTextFocus = IsActive && words.IsKeyboardFocusWithin;
            int focusedDocument = displayed, caret = words.CaretIndex;
            int selectionStart = words.SelectionStart, selectionEnd = words.SelectionEnd;
            prepare?.Invoke();
            SetBusy(true);
            Say(message);
            bool success;
            string failure = null;
            try { success = await Task.Run(work); }
            catch (Exception error) { success = false; failure = "Editor operation failed: " + error.Message; }
            if (Over || generation != lifetime) return false;
            SetBusy(false);
            Mark();
            complete?.Invoke(success);
            if (hadTextFocus && IsActive && !Over && !animation.Leaving && current == focusedDocument && displayed == focusedDocument)
            {
                words.Focus();
                int length = words.Text?.Length ?? 0;
                words.CaretIndex = Math.Clamp(caret, 0, length);
                words.SelectionStart = Math.Clamp(selectionStart, 0, length);
                words.SelectionEnd = Math.Clamp(selectionEnd, 0, length);
            }
            if (failure != null) Say(failure);
            return success;
        }
        private void SetBusy(bool value)
        {
            Busy = value;
            words.IsEnabled = !value;
            words.IsReadOnly = value || docs[current].ReadOnly;
            foreach (var tab in tabs) tab.IsEnabled = !value;
            save.IsEnabled = reload.IsEnabled = keep.IsEnabled = close.IsEnabled = !value;
            page.IsHitTestVisible = !value && !animation.Leaving;
        }
        private Task<bool> Take(int which, bool finishOpening = true)
        {
            return Operate(() => { if (finishOpening) FinishOpen(); KeepCurrent(); current = which; },
                () => { docs[which].Read(); return docs[which].Problem == null; },
                _ => { ShowCurrent(); FocusEditing(); }, "Reading " + docs[which].Label + "…");
        }
        private void QueueActivationRefresh()
        {
            if (!started || Busy || guarded || dirty[current] || activationRefreshQueued) return;
            activationRefreshQueued = true;
            // A window activation can precede the click which focused its text.
            // Let that input complete before taking the focus/selection snapshot.
            Dispatcher.UIThread.Post(async () =>
            {
                activationRefreshQueued = false;
                if (!Over && IsActive && !Busy && !guarded && !dirty[current]) await Take(current);
            }, DispatcherPriority.Background);
        }
        private void FocusEditing()
        {
            if (IsActive && !Over && !animation.Leaving) words.Focus();
        }
        private void ShowCurrent()
        {
            words.IsReadOnly = docs[current].ReadOnly;
            // An unchanged activation read must retain the native undo stack,
            // selection and caret instead of assigning the same Text again.
            if (displayed != current || !string.Equals(words.Text ?? "", docs[current].Draft, StringComparison.Ordinal))
            {
                loading = true; words.IsUndoEnabled = false; words.Text = docs[current].Draft; words.SelectionStart = words.SelectionEnd = 0; words.IsUndoEnabled = true; loading = false;
            }
            displayed = current;
            heading.Text = docs[current].Label; AutomationProperties.SetName(words, docs[current].Label);
            Mark(); if (!guarded) Say(docs[current].Problem ?? docs[current].Notice ?? "");
        }
        private void Mark()
        {
            for (int i = 0; i < tabs.Length; i++) { dirty[i] = docs[i].Dirty; tabs[i].Text = docs[i].Label + (dirty[i] ? " *" : ""); tabs[i].Filled = i == current; tabs[i].InvalidateVisual(); }
            // The compositor policy binds this title with PID/class/address. The
            // document name and dirty state live in the heading and tabs; changing
            // WM_NAME here would invalidate that native identity after every edit.
        }
        private void Say(string text) { status.Text = text; ToolTip.SetTip(status, text); }
        internal Task<bool> Save()
        {
            int selected = 0; string error = null;
            return Operate(() => { FinishOpen(); KeepCurrent(); selected = current; },
                () => { error = docs[selected].Save(); return error == null; },
                ok => { Say(error ?? docs[selected].Notice ?? "Saved."); if (ok && selected == 0) Saved?.Invoke(); }, "Saving…");
        }
        internal Task<bool> Reload()
        {
            int selected = 0;
            return Operate(() => { KeepCurrent(); selected = current; }, () => docs[selected].Reload(),
                _ => { ShowCurrent(); FocusEditing(); }, "Reloading…");
        }
        internal void Ask(Action then = null)
        {
            if (Over) { then?.Invoke(); return; }
            if (Busy) { _ = AskWhenReady(then); return; }
            if (animation.Leaving) { if (then != null) after = then; return; }
            FinishOpen(); KeepCurrent(); Mark(); after = then;
            if (Dirty)
            {
                guarded = true; save.Text = "Save + close"; reload.Text = "Discard"; keep.IsVisible = true; PlaceFoot();
                Say("Unsaved changes in " + string.Join(" and ", docs.Where(d => d.Dirty).Select(d => d.Label)) + "."); Activate(); return;
            }
            Depart();
        }
        private async Task AskWhenReady(Action then) { await pending; Ask(then); }
        internal bool Clear(Action then)
        {
            if (Over) return true;
            Ask(then); return false;
        }
        internal Task<bool> GuardSave()
        {
            int failed = -1; bool personality = false; string error = null;
            return Operate(KeepCurrent, () => {
                for (int i = 0; i < docs.Length; i++)
                {
                    if (!docs[i].Dirty) continue;
                    error = docs[i].Save();
                    if (error != null) { failed = i; return false; }
                    if (i == 0) personality = true;
                }
                return true;
            }, ok => {
                if (personality) Saved?.Invoke();
                if (!ok) { if (failed >= 0) { current = failed; ShowCurrent(); } Say((error ?? "Couldn't save the notes.") + " Your changes are still here."); return; }
                guarded = false; Depart();
            }, "Saving changes…");
        }
        internal Task<bool> GuardDiscard()
        {
            string error = null;
            return Operate(KeepCurrent, () => {
                foreach (var doc in docs) { doc.Discard(); if (doc.Notice != null) { error = doc.Notice; return false; } }
                return true;
            }, ok => { if (!ok) { ShowCurrent(); Say(error ?? "Couldn't discard the recovery draft."); return; } guarded = false; Depart(); }, "Discarding changes…");
        }
        internal void GuardKeep()
        {
            if (Busy) return;
            guarded = false; after = null; save.Text = "Save"; reload.Text = "Reload"; keep.IsVisible = false; PlaceFoot(); Say("Changes kept here."); FocusEditing();
        }
        private void Depart()
        {
            animation.Close(); if (!Motion) { animation.Advance(10); } StartClock();
            if (animation.Stage == NativeEditorMotion.Phase.Gone) Tick();
        }
        internal async Task<string> KeepDraftsAsync()
        {
            string result = null;
            bool kept = await Operate(KeepCurrent, () => {
                string[] errors = docs.Select(d => d.KeepDraft()).Where(e => e != null).ToArray();
                result = errors.Length == 0 ? null : string.Join(" ", errors); return result == null;
            }, _ => { if (result != null) Say(result); }, "Keeping unsaved drafts…");
            return result ?? (kept ? null : "Couldn't keep the unsaved file drafts. The editor remains open.");
        }
        internal async Task<string> PrepareShutdownAsync()
        {
            if (Over) return null;
            string error = await KeepDraftsAsync();
            if (error == null) permitted = true;
            return error;
        }
        private void OnClosing(object sender, WindowClosingEventArgs e)
        {
            if (permitted) return;
            // Normal application shutdown is deferred by Pet.Finish until this lane
            // finishes. A compositor/OS shutdown may kill the process regardless;
            // cancel this close while recovery is pending instead of blocking UI.
            e.Cancel = true;
            if (e.CloseReason == WindowCloseReason.ApplicationShutdown || e.CloseReason == WindowCloseReason.OSShutdown) _ = RecoverAndClose();
            else Ask();
        }
        private async Task RecoverAndClose()
        {
            string error = await KeepDraftsAsync();
            if (error != null) { Brain.Log("file-editor-draft", -1, error); return; }
            permitted = true; Close();
        }

        private sealed class EditorButton : Button
        {
            internal bool Filled, Cross;
            internal string Text { get => Content as string ?? ""; set { Content = value; InvalidateVisual(); } }
            internal EditorButton()
            {
                MinWidth = MinHeight = 0; Padding = new Thickness(0); FontFamily = TitleFace; FontSize = 14;
                Cursor = new Cursor(StandardCursorType.Hand);
                Template = new FuncControlTemplate<Button>((_, _) => new Panel { Background = Brushes.Transparent });
                PropertyChanged += (_, _) => InvalidateVisual();
            }
            public override void Render(DrawingContext c)
            {
                double w = Bounds.Width, h = Bounds.Height;
                bool lit = Filled ? !(IsPointerOver || IsPressed) : IsPointerOver || IsPressed;
                IBrush red = new SolidColorBrush(NativeEditorArt.Red), dark = new SolidColorBrush(NativeEditorArt.Dark);
                if (Cross)
                {
                    if (IsPointerOver || IsPressed) c.DrawRectangle(red, null, new Rect(Bounds.Size));
                    var p = new Pen(IsPointerOver || IsPressed ? dark : red, 2);
                    c.DrawLine(p, new Point(w*.3,h*.3),new Point(w*.7,h*.7));c.DrawLine(p,new Point(w*.3,h*.7),new Point(w*.7,h*.3));return;
                }
                var shape = MainframeTheme.Cut(new Rect(.5,.5,Math.Max(0,w-1),Math.Max(0,h-1)), br:h/4);
                c.DrawGeometry(lit ? red : null, new Pen(red, 1), shape);
                var text = new FormattedText(Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(TitleFace,FontStyle.Normal,FontWeight.Bold),14,lit?dark:red);
                c.DrawText(text,new Point(Math.Max(0,(w-text.Width)/2),Math.Max(0,(h-text.Height)/2)));
                if (IsKeyboardFocusWithin) c.DrawRectangle(null,new Pen(lit?dark:red,1),new Rect(3,3,Math.Max(0,w-6),Math.Max(0,h-6)));
            }
        }

        private sealed class ArtLayer : Control
        {
            internal double Frame = 1, Scale = 1.6;
            internal bool Shadow;
            private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString() + "V";
            public override void Render(DrawingContext c)
            {
                double f = Frame, a = Scale; if (f <= 0) return;
                IBrush red = Shadow ? Brushes.Black : new SolidColorBrush(NativeEditorArt.Red);
                var spine=NativeEditorArt.Shape(NativeEditorArt.Spine(f),a);
                c.DrawGeometry(Shadow ? Brushes.Black : new SolidColorBrush(Color.FromArgb(243,222,70,62)),null,spine);
                if (!Shadow) c.DrawGeometry(NativeEditorArt.Grain,null,spine);
                if (NativeEditorArt.Along(NativeEditorArt.SheetEnds,14,f)>NativeEditorArt.SheetLeft)
                {
                    var sheet=NativeEditorArt.Shape(NativeEditorArt.Sheet(f),a);c.DrawGeometry(Shadow ? Brushes.Black : new SolidColorBrush(NativeEditorArt.SheetFill(f)),null,sheet);
                    if (!Shadow) c.DrawGeometry(NativeEditorArt.Grain,null,sheet);
                    c.DrawGeometry(null,new Pen(Shadow ? Brushes.Black : new SolidColorBrush(Color.FromArgb(220,235,74,66)),2.4*a),NativeEditorArt.Shape(NativeEditorArt.Sheet(f,1.2),a));
                }
                if (!Shadow && NativeEditorArt.Along(NativeEditorArt.SpineEnds,1,f)>NativeEditorArt.TickY+2)
                    c.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(170*NativeEditorArt.Grown(f,6,14)),0,0,0)),null,Box(141,NativeEditorArt.TickY,148,NativeEditorArt.TickY+1,a));
                double rule=f<21?NativeEditorArt.RuleLeft:NativeEditorArt.Along(NativeEditorArt.RuleEnds,21,f),block=f<25?NativeEditorArt.RuleLeft:NativeEditorArt.Along(NativeEditorArt.BlockEnds,25,f);
                c.DrawRectangle(Shadow ? Brushes.Black : new SolidColorBrush(Color.FromArgb(222,235,74,66)),null,Box(NativeEditorArt.RuleLeft,NativeEditorArt.RuleTop,rule,NativeEditorArt.RuleBottom,a));
                c.DrawRectangle(Shadow ? Brushes.Black : new SolidColorBrush(Color.FromArgb(222,235,74,66)),null,Box(NativeEditorArt.RuleLeft,NativeEditorArt.BlockTop,block,NativeEditorArt.BlockBottom,a));
                Text(c,Typed(Version,f,30),NativeEditorArt.RuleLeft,67,11.5*a,red,a);
                Text(c,Typed("CAPRI",f,30),NativeEditorArt.RuleLeft,83,20*a,red,a);
                Text(c,Typed("HER FILES",f,35),NativeEditorArt.BlockEnd+8,NativeEditorArt.BlockTop-.5,11.5*a,red,a);
                if (f>13)
                {
                    using(c.PushTransform(Matrix.CreateRotation(-Math.PI/2)*Matrix.CreateTranslation((NativeEditorArt.SpineLeft+12-NativeEditorArt.Left)*a,(NativeEditorArt.Foot-14-NativeEditorArt.Top)*a)))
                    {var t=new FormattedText(Typed(Version,f,15),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(TitleFace,FontStyle.Normal,FontWeight.Bold),12.5*a,Brushes.Black);c.DrawText(t,new Point(0,0));}
                }
            }
            private static Rect Box(double l,double t,double r,double b,double a)=>new((l-NativeEditorArt.Left)*a,(t-NativeEditorArt.Top)*a,Math.Max(0,r-l)*a,Math.Max(0,b-t)*a);
            private static string Typed(string text,double frame,int at)=>text.Substring(0,Math.Clamp((int)Math.Floor((frame-at)*1.5)+1,0,text.Length));
            private static void Text(DrawingContext c,string text,double x,double y,double size,IBrush ink,double a)
            {if(text.Length==0)return;var t=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(TitleFace,FontStyle.Normal,FontWeight.Bold),size,ink);c.DrawText(t,new Point((x-NativeEditorArt.Left)*a,(y-NativeEditorArt.Top)*a));}
        }
    }
}
