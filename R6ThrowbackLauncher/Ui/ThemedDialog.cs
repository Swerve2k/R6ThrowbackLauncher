using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace R6ThrowbackLauncher.Ui
{
    public sealed record DialogButton(string Text, string Result, bool Primary = false, bool IsCancel = false);

    /// <summary>Monochrome replacement for MessageBox and small modal forms.</summary>
    public sealed class ThemedDialog : Window
    {
        public string? Result { get; private set; }

        private ThemedDialog(Window? owner, string title, UIElement body, IReadOnlyList<DialogButton> buttons, double width)
        {
            Owner = owner;
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = owner == null;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            SetResourceReference(FontFamilyProperty, "Font.Ui");
            UseLayoutRounding = true;

            var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
            stack.Children.Add(UiFactory.Text(title, 16, "Brush.Fg", FontWeights.SemiBold, wrap: true).Margin(0, 0, 0, 10));
            stack.Children.Add(body);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            foreach (var b in buttons)
            {
                var btn = UiFactory.Button(b.Text, b.Primary ? "Btn.Primary" : "Btn.Secondary");
                btn.MinWidth = 88;
                btn.Margin = new Thickness(8, 0, 0, 0);
                btn.IsDefault = b.Primary;
                btn.IsCancel = b.IsCancel;
                string result = b.Result;
                btn.Click += (_, _) => { Result = result; Close(); };
                bar.Children.Add(btn);
            }
            stack.Children.Add(bar);

            var frame = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16),
                Child = stack,
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.55, Color = Colors.Black },
            };
            frame.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, "Brush.StrokeStrong");
            frame.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Content = frame;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { Result = null; Close(); }
            };
            Loaded += (_, _) => (owner as IScrimHost)?.SetScrim(true);
            Closed += (_, _) => (owner as IScrimHost)?.SetScrim(false);
        }

        public static string? Show(Window? owner, string title, UIElement body, IReadOnlyList<DialogButton> buttons, double width = 460)
        {
            var dlg = new ThemedDialog(owner ?? ActiveWindow(), title, body, buttons, width);
            dlg.ShowDialog();
            return dlg.Result;
        }

        public static bool Confirm(Window? owner, string title, string message, string confirm = "Continue", string cancel = "Cancel")
        {
            return Show(owner, title, Message(message), new[]
            {
                new DialogButton(cancel, "cancel", IsCancel: true),
                new DialogButton(confirm, "ok", Primary: true),
            }) == "ok";
        }

        public static void Alert(Window? owner, string title, string message, string ok = "OK")
        {
            Show(owner, title, Message(message), new[] { new DialogButton(ok, "ok", Primary: true, IsCancel: true) });
        }

        public static string? Prompt(Window? owner, string title, string message, string initial = "", string placeholder = "",
            Func<string, string?>? validate = null, string confirm = "Save", bool password = false)
        {
            var body = new StackPanel();
            if (!string.IsNullOrEmpty(message)) body.Children.Add(Message(message).Margin(0, 0, 0, 12));
            Control input;
            if (password)
            {
                var pb = new PasswordBox();
                pb.Styled("Input.Password");
                input = pb;
            }
            else
            {
                input = UiFactory.Input(initial, placeholder);
            }
            body.Children.Add(input);
            var error = UiFactory.Text("", 12, "Brush.Fg2", wrap: true).Margin(0, 8, 0, 0);
            error.Visibility = Visibility.Collapsed;
            body.Children.Add(error);

            string? value = null;
            var dlg = new ThemedDialog(owner ?? ActiveWindow(), title, body, new[]
            {
                new DialogButton("Cancel", "cancel", IsCancel: true),
                new DialogButton(confirm, "ok", Primary: true),
            }, 440);
            dlg.Loaded += (_, _) => { input.Focus(); (input as TextBox)?.SelectAll(); };

            dlg.Closing += (_, e) =>
            {
                if (dlg.Result != "ok") return;
                string text = input is PasswordBox p ? p.Password : ((TextBox)input).Text.Trim();
                string? problem = validate?.Invoke(text);
                if (problem != null)
                {
                    error.Text = problem;
                    error.Visibility = Visibility.Visible;
                    dlg.Result = null;
                    e.Cancel = true;
                    return;
                }
                value = text;
            };
            dlg.ShowDialog();
            return value;
        }

        public static TextBlock Message(string text) => UiFactory.Text(text, 13, "Brush.Fg2", wrap: true);

        private static Window? ActiveWindow()
        {
            foreach (Window w in Application.Current.Windows)
                if (w.IsActive) return w;
            return Application.Current.MainWindow;
        }
    }

    public interface IScrimHost
    {
        void SetScrim(bool visible);
    }
}
