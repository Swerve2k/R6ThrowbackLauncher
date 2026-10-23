using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace R6ThrowbackLauncher.Ui
{
    public enum ArtworkMode { Hover, Color, Mono }

    /// <summary>Decodes season artwork once per size and keeps colour and greyscale copies.</summary>
    public static class ArtworkCache
    {
        private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ArtworkMode Mode { get; set; } = ArtworkMode.Color;

        public static BitmapSource? Get(string path, int decodeWidth, bool grayscale = false)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string key = $"{path}|{decodeWidth}|{grayscale}";
            if (Cache.TryGetValue(key, out var hit)) return hit;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = decodeWidth;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                BitmapSource result = bmp;
                if (grayscale)
                {
                    var gray = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
                    gray.Freeze();
                    result = gray;
                }
                Cache[key] = result;
                return result;
            }
            catch { return null; }
        }

        /// <summary>
        /// Artwork layer: greyscale with colour fading in on hover (Hover mode), or a single image.
        /// <paramref name="hoverSource"/> is the element whose mouse-over reveals colour and zooms the art
        /// by <paramref name="zoom"/> (1 turns the zoom off).
        /// </summary>
        public static Grid Build(string path, int decodeWidth, UIElement? hoverSource, bool forceColor = false, double zoom = 1.06)
        {
            var grid = new Grid { ClipToBounds = true };
            grid.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface2");
            var mode = forceColor ? ArtworkMode.Color : Mode;

            // Images sit in their own layer so the zoom stays inside the clipped frame.
            var layer = new Grid { RenderTransformOrigin = new Point(0.5, 0.5) };
            grid.Children.Add(layer);

            var baseImg = new Image
            {
                Source = Get(path, decodeWidth, grayscale: mode != ArtworkMode.Color),
                Stretch = Stretch.UniformToFill,
            };
            RenderOptions.SetBitmapScalingMode(baseImg, BitmapScalingMode.HighQuality);
            layer.Children.Add(baseImg);

            if (hoverSource == null) return grid;

            if (mode == ArtworkMode.Hover)
            {
                var colorImg = new Image
                {
                    Source = Get(path, decodeWidth, grayscale: false),
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0,
                };
                RenderOptions.SetBitmapScalingMode(colorImg, BitmapScalingMode.HighQuality);
                layer.Children.Add(colorImg);
                hoverSource.MouseEnter += (_, _) => Fade(colorImg, 1);
                hoverSource.MouseLeave += (_, _) => Fade(colorImg, 0);
            }

            if (zoom > 1)
            {
                var scale = new ScaleTransform(1, 1);
                layer.RenderTransform = scale;
                hoverSource.MouseEnter += (_, _) => Zoom(scale, zoom);
                hoverSource.MouseLeave += (_, _) => Zoom(scale, 1);
            }
            return grid;
        }

        private static void Zoom(ScaleTransform scale, double to)
        {
            if (Themes.ThemeManager.ReduceMotion)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                return;
            }
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 1 ? 320 : 260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private static void Fade(UIElement element, double to)
        {
            if (Themes.ThemeManager.ReduceMotion)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = to;
                return;
            }
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
