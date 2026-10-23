using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace R6ThrowbackLauncher.Ui
{
    /// <summary>Builds theme-aware elements for code-generated UI.</summary>
    public static class UiFactory
    {
        public static T Fg<T>(this T element, string brushKey) where T : FrameworkElement
        {
            switch (element)
            {
                case TextBlock tb: tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey); break;
                case Control c: c.SetResourceReference(Control.ForegroundProperty, brushKey); break;
            }
            return element;
        }

        public static T Bg<T>(this T element, string brushKey) where T : FrameworkElement
        {
            switch (element)
            {
                case Border b: b.SetResourceReference(Border.BackgroundProperty, brushKey); break;
                case Panel p: p.SetResourceReference(Panel.BackgroundProperty, brushKey); break;
                case Control c: c.SetResourceReference(Control.BackgroundProperty, brushKey); break;
            }
            return element;
        }

        public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
        {
            element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            return element;
        }

        public static T Margin<T>(this T element, double l, double t, double r, double b) where T : FrameworkElement
        {
            element.Margin = new Thickness(l, t, r, b);
            return element;
        }

        public static T Col<T>(this T element, int column, int span = 1) where T : UIElement
        {
            Grid.SetColumn(element, column);
            if (span > 1) Grid.SetColumnSpan(element, span);
            return element;
        }

        public static T Row<T>(this T element, int row, int span = 1) where T : UIElement
        {
            Grid.SetRow(element, row);
            if (span > 1) Grid.SetRowSpan(element, span);
            return element;
        }

        public static T Tip<T>(this T element, string tip) where T : FrameworkElement
        {
            element.ToolTip = tip;
            return element;
        }

        public static TextBlock Text(string text, double size = 13, string brush = "Brush.Fg", FontWeight? weight = null,
            bool wrap = false, bool trim = true)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = trim && !wrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return tb.Fg(brush);
        }

        public static TextBlock Section(string text) => Text(text, 11, "Brush.Fg3", FontWeights.SemiBold);

        public static TextBlock Icon(string glyph, double size = 15, string brush = "Brush.Fg2")
        {
            var tb = new TextBlock
            {
                Text = glyph,
                FontSize = size,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            return tb.Fg(brush);
        }

        public static Border Card(UIElement? child = null, double padding = 20)
        {
            var b = new Border { Child = child, Padding = new Thickness(padding) };
            b.Styled("Card");
            b.Padding = new Thickness(padding);
            return b;
        }

        public static Border Divider(double top = 12, double bottom = 12) =>
            new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom) }.Bg("Brush.Stroke");

        public static Border Chip(string text, string bg = "Brush.Surface3", string fg = "Brush.Fg2")
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Text(text, 11, fg, FontWeights.SemiBold),
            };
            return b.Bg(bg);
        }

        public static Button Button(string text, string style = "Btn.Secondary", RoutedEventHandler? click = null, string? icon = null)
        {
            var btn = new Button { Content = Content(text, icon) };
            btn.Styled(style);
            if (click != null) btn.Click += click;
            return btn;
        }

        public static Button IconButton(string glyph, string tooltip, RoutedEventHandler? click = null)
        {
            var btn = new Button { Content = glyph, ToolTip = tooltip };
            btn.Styled("Btn.Icon");
            AutomationName(btn, tooltip);
            if (click != null) btn.Click += click;
            return btn;
        }

        public static object Content(string text, string? icon)
        {
            if (icon == null) return text;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new TextBlock { Text = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            ic.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            sp.Children.Add(ic);
            if (!string.IsNullOrEmpty(text))
                sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            else ic.Margin = new Thickness(0);
            return sp;
        }

        public static void AutomationName(DependencyObject d, string name) =>
            System.Windows.Automation.AutomationProperties.SetName(d, name);

        public static CheckBox Switch(bool isOn, Action<bool> changed)
        {
            var cb = new CheckBox { IsChecked = isOn };
            cb.Styled("Switch");
            cb.Checked += (_, _) => changed(true);
            cb.Unchecked += (_, _) => changed(false);
            return cb;
        }

        public static TextBox Input(string text = "", string placeholder = "")
        {
            var tb = new TextBox { Text = text, Tag = placeholder };
            tb.Styled("Input");
            return tb;
        }

        public static ProgressBar Progress(double height = 6)
        {
            var pb = new ProgressBar { Minimum = 0, Maximum = 100, Height = height };
            pb.SetResourceReference(FrameworkElement.StyleProperty, "Progress.Active");
            return pb;
        }

        /// <summary>Settings-style row: label and description on the left, control on the right.</summary>
        public static Grid SettingRow(string label, string? hint, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6), MinHeight = 34 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
            sp.Children.Add(Text(label, 13, "Brush.Fg", FontWeights.Normal));
            if (!string.IsNullOrEmpty(hint))
                sp.Children.Add(Text(hint, 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 0));
            g.Children.Add(sp);
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(control.Col(1));
            return g;
        }

        public static StackPanel HStack(double spacing, params UIElement[] children)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0 && children[i] is FrameworkElement fe && fe.Margin == default)
                    fe.Margin = new Thickness(spacing, 0, 0, 0);
                sp.Children.Add(children[i]);
            }
            return sp;
        }

        public static StackPanel VStack(params UIElement[] children)
        {
            var sp = new StackPanel();
            foreach (var c in children) sp.Children.Add(c);
            return sp;
        }

        public static Border EmptyState(string glyph, string title, string message, UIElement? action = null)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 48) };
            sp.Children.Add(Icon(glyph, 30, "Brush.Fg3").Margin(0, 0, 0, 14));
            sp.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }.Fg("Brush.Fg"));
            sp.Children.Add(new TextBlock
            {
                Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                MaxWidth = 420, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            }.Fg("Brush.Fg3"));
            if (action is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 18, 0, 0);
                fe.HorizontalAlignment = HorizontalAlignment.Center;
                sp.Children.Add(fe);
            }
            return new Border { Child = sp };
        }

        public static void OpenMenu(UIElement target, params object[] items)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var item in items) menu.Items.Add(item);
            menu.IsOpen = true;
        }

        public static MenuItem MenuItem(string header, string? icon, Action onClick, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            if (icon != null) mi.Icon = new TextBlock { Text = icon };
            mi.Click += (_, _) => onClick();
            return mi;
        }

        public static void Hover(Border border, string normalKey, string hoverKey)
        {
            border.Bg(normalKey);
            border.MouseEnter += (_, _) => border.Bg(hoverKey);
            border.MouseLeave += (_, _) => border.Bg(normalKey);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):0.00} TB";
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.0} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0} MB";
            if (bytes >= 1L << 10) return $"{bytes / 1024.0:0} KB";
            return $"{bytes} B";
        }

        public static Cursor Hand => Cursors.Hand;
    }
}
