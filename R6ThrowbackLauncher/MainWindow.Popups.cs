using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using R6ThrowbackLauncher.Themes;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    /// <summary>In-window announcement popups and the full-window update screen.</summary>
    public partial class MainWindow
    {
        private enum PopupKind { Info, Update, Warning }

        private sealed record PopupAction(string Text, string Result, bool Primary = false, string? Icon = null);

        private readonly SemaphoreSlim _popupGate = new(1, 1);
        private TaskCompletionSource<string?>? _popupResult;
        private Border? _popupCard;
        private Border? _popupScrim;

        /// <summary>Shows a popup and waits for it to close. Popups queue up and appear one at a time.</summary>
        private async Task<string?> ShowPopupAsync(PopupKind kind, string eyebrow, string title, UIElement body, params PopupAction[] actions)
        {
            await _popupGate.WaitAsync();
            try
            {
                while (UpdateLayer.Visibility == Visibility.Visible) await Task.Delay(500);
                var tcs = new TaskCompletionSource<string?>();
                _popupResult = tcs;
                BuildPopup(kind, eyebrow, title, body, actions);
                return await tcs.Task;
            }
            finally { _popupGate.Release(); }
        }

        private void BuildPopup(PopupKind kind, string eyebrow, string title, UIElement body, PopupAction[] actions)
        {
            PopupLayer.Children.Clear();
            var scrim = new Border { Opacity = 0 }.Bg("Brush.Scrim");
            scrim.MouseLeftButtonDown += (_, _) => ClosePopup(null);
            PopupLayer.Children.Add(scrim);

            var card = new Border
            {
                Width = 540,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Opacity = 0,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.5, Color = Colors.Black },
            };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.StrokeStrong");

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Animated band with the emblem
            var band = new Grid { Height = 136, ClipToBounds = true };
            band.Clip = new RectangleGeometry(new Rect(0, 0, 538, 136), 15, 15);
            var bandBg = new Border { CornerRadius = new CornerRadius(15, 15, 0, 0) }.Bg("Brush.Surface2");
            band.Children.Add(bandBg);
            var stripes = new Rectangle { Opacity = 0.09, IsHitTestVisible = false };
            stripes.SetResourceReference(Shape.FillProperty, "Brush.ProgressStripes");
            band.Children.Add(stripes);
            string glyph = kind switch { PopupKind.Update => Icons.Download, PopupKind.Warning => Icons.Warning, _ => Icons.Megaphone };
            band.Children.Add(Emblem(glyph, 64));
            var chip = UiFactory.Chip(eyebrow, "Brush.Accent", "Brush.OnAccent");
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.VerticalAlignment = VerticalAlignment.Top;
            chip.Margin = new Thickness(20, 18, 0, 0);
            band.Children.Add(chip);
            var close = UiFactory.IconButton(Icons.Close, "Close", (_, _) => ClosePopup(null));
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new Thickness(0, 10, 10, 0);
            band.Children.Add(close);
            var rule = UiFactory.Divider(0, 0);
            rule.VerticalAlignment = VerticalAlignment.Bottom;
            band.Children.Add(rule);
            root.Children.Add(band);

            var content = new StackPanel { Margin = new Thickness(28, 22, 28, 0) };
            content.Children.Add(UiFactory.Text(title, 22, "Brush.Fg", FontWeights.Bold, wrap: true));
            if (body is FrameworkElement fe) fe.Margin = new Thickness(0, 10, 0, 0);
            content.Children.Add(body);
            root.Children.Add(content.Row(1));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(28, 22, 28, 24) };
            Button? primary = null;
            foreach (var a in actions)
            {
                var action = a;
                var b = UiFactory.Button(a.Text, a.Primary ? "Btn.Primary" : "Btn.Secondary", (_, _) => ClosePopup(action.Result), a.Icon);
                b.Margin = new Thickness(buttons.Children.Count > 0 ? 8 : 0, 0, 0, 0);
                b.MinWidth = 96;
                if (a.Primary) primary = b;
                buttons.Children.Add(b);
            }
            root.Children.Add(buttons.Row(2));

            card.Child = root;
            PopupLayer.Children.Add(card);
            PopupLayer.Visibility = Visibility.Visible;
            _popupCard = card;
            _popupScrim = scrim;
            card.Loaded += (_, _) => primary?.Focus();
            if (WindowState == WindowState.Minimized || (!IsVisible && _trayIcon != null)) RestoreFromTray();

            if (ThemeManager.ReduceMotion)
            {
                scrim.Opacity = 1;
                card.Opacity = 1;
                return;
            }
            scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            var scale = new ScaleTransform(0.92, 0.92);
            var lift = new TranslateTransform(0, 18);
            card.RenderTransform = new TransformGroup { Children = { scale, lift } };
            var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void ClosePopup(string? result)
        {
            var tcs = _popupResult;
            if (tcs == null) return;
            _popupResult = null;
            var card = _popupCard;
            var scrim = _popupScrim;

            void Finish()
            {
                PopupLayer.Children.Clear();
                PopupLayer.Visibility = Visibility.Collapsed;
                tcs.TrySetResult(result);
            }

            if (ThemeManager.ReduceMotion || card == null || scrim == null) { Finish(); return; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) => Finish();
            scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            card.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Accent disc with a glyph and sonar rings.</summary>
        private static Grid Emblem(string glyph, double size)
        {
            var g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 2; i++)
            {
                var ring = new Ellipse { StrokeThickness = 1.5, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
                ring.SetResourceReference(Shape.StrokeProperty, "Brush.Fg2");
                var st = new ScaleTransform(1, 1);
                ring.RenderTransform = st;
                g.Children.Add(ring);
                if (ThemeManager.ReduceMotion) continue;
                var begin = TimeSpan.FromSeconds(i * 1.1);
                var dur = TimeSpan.FromSeconds(2.2);
                var grow = new DoubleAnimation(1, 2.1, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 0, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever });
            }
            g.Children.Add(new Border { CornerRadius = new CornerRadius(size / 2) }.Bg("Brush.Accent"));
            g.Children.Add(UiFactory.Icon(glyph, Math.Round(size * 0.4), "Brush.OnAccent"));
            return g;
        }

        // ══ Update screen ════════════════════════════════════════════════════

        /// <summary>Full-window progress screen shown while the launcher downloads and installs an update.</summary>
        private sealed class UpdateScreen
        {
            private const double Size = 200, Radius = 86;
            private static readonly (string Label, string Glyph)[] Steps =
            {
                ("Download", Icons.Download), ("Verify", Icons.Shield), ("Install", Icons.Archive), ("Restart", Icons.Refresh),
            };

            private readonly MainWindow _w;
            private readonly Grid _root = new();
            private readonly FrameworkElement _content;
            private readonly Path _arc = new() { StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            private readonly Path _spinner = new() { StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed };
            private readonly RotateTransform _spin = new(0, Size / 2, Size / 2);
            private readonly TextBlock _percent;
            private readonly TextBlock _amount;
            private readonly TextBlock _centerGlyph;
            private readonly TextBlock _title;
            private readonly TextBlock _status;
            private readonly Border[] _stepDots = new Border[Steps.Length];
            private readonly TextBlock[] _stepGlyphs = new TextBlock[Steps.Length];
            private readonly TextBlock[] _stepLabels = new TextBlock[Steps.Length];
            private readonly Border[] _connectors = new Border[Steps.Length - 1];
            private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
            private readonly Button _cancel;
            private double _shownFraction;

            public event Action? CancelRequested;

            public UpdateScreen(MainWindow window, string version)
            {
                _w = window;
                _root.SetResourceReference(Panel.BackgroundProperty, "Brush.Window");
                var stripes = new Rectangle { Opacity = 0.035, IsHitTestVisible = false };
                stripes.SetResourceReference(Shape.FillProperty, "Brush.ProgressStripes");
                _root.Children.Add(stripes);

                var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 560, RenderTransformOrigin = new Point(0.5, 0.5) };

                // Ring
                var ring = new Grid { Width = Size, Height = Size, HorizontalAlignment = HorizontalAlignment.Center };
                for (int i = 0; i < 2; i++)
                {
                    var ripple = new Ellipse { Width = Radius * 2, Height = Radius * 2, StrokeThickness = 1.5, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
                    ripple.SetResourceReference(Shape.StrokeProperty, "Brush.Fg3");
                    var st = new ScaleTransform(1, 1);
                    ripple.RenderTransform = st;
                    ring.Children.Add(ripple);
                    if (ThemeManager.ReduceMotion) continue;
                    var begin = TimeSpan.FromSeconds(i * 1.3);
                    var dur = TimeSpan.FromSeconds(2.6);
                    var grow = new DoubleAnimation(1, 1.35, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                    ripple.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.5, 0, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever });
                }
                // An Ellipse strokes inside its bounds; widen it so the track lines up with the arc's centre line.
                var track = new Ellipse { Width = Radius * 2 + 6, Height = Radius * 2 + 6, StrokeThickness = 6 };
                track.SetResourceReference(Shape.StrokeProperty, "Brush.Track");
                ring.Children.Add(track);
                _arc.SetResourceReference(Shape.StrokeProperty, "Brush.Fg");
                ring.Children.Add(_arc);
                _spinner.SetResourceReference(Shape.StrokeProperty, "Brush.Fg");
                _spinner.Data = Arc(0.22);
                _spinner.RenderTransform = _spin;
                ring.Children.Add(_spinner);

                var mid = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                _percent = UiFactory.Text("0%", 36, "Brush.Fg", FontWeights.Bold);
                _percent.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
                _percent.HorizontalAlignment = HorizontalAlignment.Center;
                mid.Children.Add(_percent);
                _amount = UiFactory.Text("Connecting", 12, "Brush.Fg3");
                _amount.HorizontalAlignment = HorizontalAlignment.Center;
                mid.Children.Add(_amount);
                ring.Children.Add(mid);
                _centerGlyph = UiFactory.Icon(Icons.Check, 54, "Brush.Fg");
                _centerGlyph.Visibility = Visibility.Collapsed;
                _centerGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
                _centerGlyph.RenderTransform = new ScaleTransform(1, 1);
                ring.Children.Add(_centerGlyph);
                center.Children.Add(ring);

                // Text
                _title = UiFactory.Text($"Updating to version {version}", 26, "Brush.Fg", FontWeights.Bold);
                _title.HorizontalAlignment = HorizontalAlignment.Center;
                _title.Margin = new Thickness(0, 30, 0, 0);
                center.Children.Add(_title);
                _status = UiFactory.Text("Getting ready", 13, "Brush.Fg2", wrap: true);
                _status.TextAlignment = TextAlignment.Center;
                _status.HorizontalAlignment = HorizontalAlignment.Center;
                _status.Margin = new Thickness(0, 8, 0, 0);
                center.Children.Add(_status);

                // Steps
                var steps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
                for (int i = 0; i < Steps.Length; i++)
                {
                    if (i > 0)
                    {
                        var line = new Border { Width = 54, Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 15, 6, 0) }.Bg("Brush.Stroke");
                        _connectors[i - 1] = line;
                        steps.Children.Add(line);
                    }
                    var col = new StackPanel { Width = 64 };
                    var dot = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center }.Bg("Brush.Surface3");
                    var g = UiFactory.Icon(Steps[i].Glyph, 14, "Brush.Fg3");
                    dot.Child = g;
                    col.Children.Add(dot);
                    var label = UiFactory.Text(Steps[i].Label, 12, "Brush.Fg3");
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.Margin = new Thickness(0, 8, 0, 0);
                    col.Children.Add(label);
                    _stepDots[i] = dot;
                    _stepGlyphs[i] = g;
                    _stepLabels[i] = label;
                    steps.Children.Add(col);
                }
                center.Children.Add(steps);

                _cancel = UiFactory.Button("Cancel", "Btn.Ghost", (_, _) => CancelRequested?.Invoke());
                _buttons.Children.Add(_cancel);
                center.Children.Add(_buttons);
                var note = UiFactory.Text("The launcher restarts by itself when the update is installed.", 12, "Brush.Fg3");
                note.HorizontalAlignment = HorizontalAlignment.Center;
                note.Margin = new Thickness(0, 14, 0, 0);
                center.Children.Add(note);

                _root.Children.Add(center);
                _content = center;
            }

            public void Show()
            {
                _w.PopupLayer.Visibility = Visibility.Collapsed;
                _w.UpdateLayer.Children.Clear();
                _w.UpdateLayer.Children.Add(_root);
                _w.UpdateLayer.Visibility = Visibility.Visible;
                Keyboard_Clear();
                if (ThemeManager.ReduceMotion) return;
                _root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                var scale = new ScaleTransform(0.94, 0.94);
                _content.RenderTransform = scale;
                var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }

            private void Keyboard_Clear() => System.Windows.Input.Keyboard.ClearFocus();

            public void Close()
            {
                void Remove()
                {
                    _w.UpdateLayer.Children.Clear();
                    _w.UpdateLayer.Visibility = Visibility.Collapsed;
                }
                if (ThemeManager.ReduceMotion) { Remove(); return; }
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
                fade.Completed += (_, _) => Remove();
                _root.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            public void SetStep(int index, string status)
            {
                _status.Text = status;
                for (int i = 0; i < Steps.Length; i++)
                {
                    bool done = i < index, active = i == index;
                    _stepDots[i].Bg(active ? "Brush.Accent" : "Brush.Surface3");
                    _stepGlyphs[i].Text = done ? Icons.Check : Steps[i].Glyph;
                    _stepGlyphs[i].Fg(active ? "Brush.OnAccent" : done ? "Brush.Fg" : "Brush.Fg3");
                    _stepLabels[i].Fg(active || done ? "Brush.Fg" : "Brush.Fg3");
                    _stepLabels[i].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                    if (i > 0) _connectors[i - 1].Bg(i <= index ? "Brush.Fg2" : "Brush.Stroke");
                    if (active && !ThemeManager.ReduceMotion)
                    {
                        var st = new ScaleTransform(1, 1, 16, 16);
                        _stepDots[i].RenderTransform = st;
                        var bump = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
                        st.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
                    }
                }
                if (index > 0)
                {
                    _percent.Visibility = Visibility.Collapsed;
                    _amount.Visibility = Visibility.Collapsed;
                }
            }

            public void SetDownload(long done, long total, double bytesPerSecond)
            {
                SetIndeterminate(false);
                double fraction = total > 0 ? Math.Clamp(done / (double)total, 0, 1) : 0;
                AnimateArc(fraction);
                _percent.Text = total > 0 ? $"{fraction * 100:0}%" : UiFactory.FormatBytes(done);
                _amount.Text = total > 0 ? $"{UiFactory.FormatBytes(done)} of {UiFactory.FormatBytes(total)}" : "Downloading";
                string text = "Downloading";
                if (bytesPerSecond > 1)
                {
                    text += $"  ·  {UiFactory.FormatBytes((long)bytesPerSecond)}/s";
                    if (total > done)
                    {
                        double secs = (total - done) / bytesPerSecond;
                        text += secs < 60 ? $"  ·  about {Math.Max(1, (int)secs)} s left" : $"  ·  about {(int)Math.Ceiling(secs / 60)} min left";
                    }
                }
                _status.Text = text;
            }

            private void AnimateArc(double fraction)
            {
                // Ease the visible arc toward the real value so small chunks don't make it jitter.
                _shownFraction += (fraction - _shownFraction) * (ThemeManager.ReduceMotion ? 1 : 0.6);
                if (fraction >= 1) _shownFraction = 1;
                _arc.Data = Arc(_shownFraction);
            }

            public void SetIndeterminate(bool on)
            {
                if (on == (_spinner.Visibility == Visibility.Visible)) return;
                _spinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                _arc.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                if (on && !ThemeManager.ReduceMotion)
                    _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
                else _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            }

            public void HideCancel() => _cancel.Visibility = Visibility.Collapsed;

            public void Complete()
            {
                SetIndeterminate(false);
                _arc.Data = Arc(1);
                _percent.Visibility = Visibility.Collapsed;
                _amount.Visibility = Visibility.Collapsed;
                _centerGlyph.Text = Icons.Check;
                _centerGlyph.Visibility = Visibility.Visible;
                _title.Text = "Update installed";
                for (int i = 0; i < Steps.Length; i++)
                {
                    _stepDots[i].Bg(i == Steps.Length - 1 ? "Brush.Accent" : "Brush.Surface3");
                    _stepGlyphs[i].Text = Icons.Check;
                    _stepGlyphs[i].Fg(i == Steps.Length - 1 ? "Brush.OnAccent" : "Brush.Fg");
                }
                if (ThemeManager.ReduceMotion) return;
                var st = (ScaleTransform)_centerGlyph.RenderTransform;
                var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }

            /// <summary>Shows the error and waits for Try again (true) or Close (false).</summary>
            public Task<bool> FailAsync(string message)
            {
                var tcs = new TaskCompletionSource<bool>();
                SetIndeterminate(false);
                _arc.Visibility = Visibility.Collapsed;
                _percent.Visibility = Visibility.Collapsed;
                _amount.Visibility = Visibility.Collapsed;
                _centerGlyph.Text = Icons.Warning;
                _centerGlyph.FontSize = 46;
                _centerGlyph.Visibility = Visibility.Visible;
                _title.Text = "The update didn't finish";
                _status.Text = message + "\nNothing was changed. You can try again or download it from GitHub.";
                _status.Fg("Brush.Fg");
                _buttons.Children.Clear();
                _buttons.Children.Add(UiFactory.Button("Close", "Btn.Secondary", (_, _) => tcs.TrySetResult(false)));
                _buttons.Children.Add(UiFactory.Button("Try again", "Btn.Primary", (_, _) => tcs.TrySetResult(true), Icons.Refresh).Margin(8, 0, 0, 0));
                return tcs.Task;
            }

            private static Geometry Arc(double fraction)
            {
                fraction = Math.Clamp(fraction, 0, 1);
                double c = Size / 2;
                if (fraction <= 0.0005) return Geometry.Empty;
                if (fraction >= 0.9995) return new EllipseGeometry(new Point(c, c), Radius, Radius);
                double angle = fraction * 2 * Math.PI;
                var start = new Point(c, c - Radius);
                var end = new Point(c + Radius * Math.Sin(angle), c - Radius * Math.Cos(angle));
                var fig = new PathFigure { StartPoint = start, IsClosed = false };
                fig.Segments.Add(new ArcSegment(end, new Size(Radius, Radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                geo.Freeze();
                return geo;
            }
        }
    }
}
