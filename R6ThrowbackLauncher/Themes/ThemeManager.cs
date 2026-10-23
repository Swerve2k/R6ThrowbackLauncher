using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace R6ThrowbackLauncher.Themes
{
    public static class ThemeManager
    {
        public const string Dark  = "dark";
        public const string Light = "light";

        private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
        {
            ["Brush.Window"]        = ("#0A0A0A", "#F4F4F4"),
            ["Brush.Sidebar"]       = ("#0F0F0F", "#EBEBEB"),
            ["Brush.Surface"]       = ("#141414", "#FFFFFF"),
            ["Brush.Surface2"]      = ("#1B1B1B", "#F2F2F2"),
            ["Brush.Surface3"]      = ("#242424", "#E3E3E3"),
            ["Brush.Stroke"]        = ("#262626", "#DADADA"),
            ["Brush.StrokeStrong"]  = ("#3A3A3A", "#BDBDBD"),
            ["Brush.Fg"]            = ("#F5F5F5", "#0A0A0A"),
            ["Brush.Fg2"]           = ("#A3A3A3", "#4D4D4D"),
            ["Brush.Fg3"]           = ("#6B6B6B", "#858585"),
            ["Brush.FgDisabled"]    = ("#474747", "#B8B8B8"),
            ["Brush.Accent"]        = ("#F5F5F5", "#111111"),
            ["Brush.AccentHover"]   = ("#FFFFFF", "#2B2B2B"),
            ["Brush.AccentPressed"] = ("#D4D4D4", "#3D3D3D"),
            ["Brush.OnAccent"]      = ("#0A0A0A", "#FFFFFF"),
            ["Brush.Hover"]         = ("#14FFFFFF", "#0F000000"),
            ["Brush.Pressed"]       = ("#22FFFFFF", "#1A000000"),
            ["Brush.Selected"]      = ("#1F1F1F", "#DEDEDE"),
            ["Brush.Scrim"]         = ("#B8000000", "#73000000"),
            ["Brush.Stripe"]        = ("#26000000", "#33FFFFFF"),
            ["Brush.Track"]         = ("#1F1F1F", "#E0E0E0"),
        };

        public static string Current { get; private set; } = Dark;
        public static bool ReduceMotion { get; private set; }
        public static bool IsDark => Current == Dark;

        public static void Apply(string mode)
        {
            Current = mode == Light ? Light : Dark;
            var res = Application.Current.Resources;
            foreach (var (key, (dark, light)) in Palette)
            {
                var color = (Color)ColorConverter.ConvertFromString(Current == Dark ? dark : light);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                res[key] = brush;
            }
            BuildStripes();
        }

        public static void SetReduceMotion(bool reduce)
        {
            ReduceMotion = reduce;
            var res = Application.Current.Resources;
            res["Progress.Active"] = res[reduce ? "Progress.Plain" : "Progress"];
            BuildStripes();
        }

        /// <summary>One shared, animated stripe brush keeps every progress bar's stripes in step.</summary>
        private static void BuildStripes()
        {
            var shift = new TranslateTransform();
            var drawing = new GeometryDrawing(
                Brush("Brush.Stripe"), null, Geometry.Parse("M0,14 L7,14 L14,7 L14,0 Z M0,0 L0,7 L7,0 Z"));
            var stripes = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 14, 14),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 14, 14),
                ViewboxUnits = BrushMappingMode.Absolute,
                Transform = shift,
            };
            if (!ReduceMotion)
                shift.BeginAnimation(TranslateTransform.XProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 14, TimeSpan.FromMilliseconds(600))
                    {
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    });
            Application.Current.Resources["Brush.ProgressStripes"] = stripes;
        }

        public static Brush Brush(string key) =>
            Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
