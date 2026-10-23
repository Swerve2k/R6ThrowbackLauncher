using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private string _browseFilter = "all";
        private string _browseSearch = "";

        private static string YearSpan(int year) => year == 1 ? "2015 – 2016" : $"{2015 + year}";

        private void BrowseSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _browseSearch = BrowseSearch.Text.Trim().ToLowerInvariant();
            BuildBrowse();
        }

        private void BrowseFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag }) _browseFilter = tag;
            BuildBrowse();
        }

        private void BuildBrowse()
        {
            if (_downloads == null) return;
            BrowseContent.Children.Clear();

            var list = _patches.AsEnumerable();
            list = _browseFilter switch
            {
                "installed" => list.Where(p => p.IsInstalled),
                "missing"   => list.Where(p => !p.IsInstalled),
                "hm"        => list.Where(p => p.IsHeatedMetal),
                _           => list,
            };
            if (_browseSearch.Length > 0)
                list = list.Where(p => p.BaseName.ToLowerInvariant().Contains(_browseSearch)
                                       || p.Code.ToLowerInvariant().Contains(_browseSearch)
                                       || (p.IsHeatedMetal && "heated metal".Contains(_browseSearch)));

            var items = list.ToList();
            int seasons = _patches.Select(p => p.Code).Distinct().Count();
            BrowseSubtitle.Text = $"{seasons} seasons from Year 1 to Year {_patches.Max(p => p.Year)}  ·  {_patches.Count(p => p.IsInstalled)} installed";

            if (items.Count == 0)
            {
                BrowseContent.Children.Add(UiFactory.EmptyState(Icons.Search, "No seasons match", "Try a different search or filter."));
                return;
            }

            foreach (var year in items.GroupBy(p => p.Year).OrderBy(g => g.Key))
            {
                var header = new Grid { Margin = new Thickness(0, BrowseContent.Children.Count == 0 ? 0 : 18, 0, 12) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(UiFactory.Text($"Year {year.Key}", 15, "Brush.Fg", FontWeights.SemiBold));
                label.Children.Add(UiFactory.Text(YearSpan(year.Key), 12, "Brush.Fg3").Margin(10, 1, 0, 0));
                header.Children.Add(label);
                header.Children.Add(UiFactory.Divider(0, 0).Col(1).Margin(16, 0, 0, 0));
                BrowseContent.Children.Add(header);

                var wrap = new WrapPanel { Margin = new Thickness(0, 0, -14, 0) };
                foreach (var p in year.OrderBy(p => p.Season).ThenBy(p => p.IsHeatedMetal))
                    wrap.Children.Add(BuildSeasonTile(p));
                BrowseContent.Children.Add(wrap);
            }
        }

        private Border BuildSeasonTile(SeasonPatch p)
        {
            var tile = new Border
            {
                Width = 172, Height = 172, Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ClipToBounds = true,
            };
            tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");
            UiFactory.AutomationName(tile, p.ShortLabel);

            var g = new Grid();
            var art = ArtworkCache.Build(p.ArtworkPath, 360, tile);
            art.Clip = new RectangleGeometry(new Rect(0, 0, 170, 170), 9, 9);
            g.Children.Add(art);
            var fade = new Border();
            fade.SetResourceReference(Border.BackgroundProperty, "Brush.ArtFadeBottom");
            g.Children.Add(fade);

            string? status = IsPatchRunning(p) ? "PLAYING"
                : _downloads.IsActive(p) ? $"{_downloads.Percent:0}%"
                : _downloads.IsQueued(p) ? "QUEUED"
                : p.IsInstalled ? "INSTALLED"
                : p.IsPartial ? "INCOMPLETE" : null;
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
            if (status != null) badges.Children.Add(OverlayChip(status, solid: p.IsInstalled || _downloads.IsActive(p)));
            if (p.IsHeatedMetal) badges.Children.Add(OverlayChip("HM").Margin(badges.Children.Count > 0 ? 5 : 0, 0, 0, 0));
            g.Children.Add(badges);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 10) };
            text.Children.Add(new TextBlock
            {
                Text = p.BaseName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            string sub = p.SizeGb > 0 ? $"{p.Code}  ·  {p.SizeGb:0.#} GB" : p.Code;
            text.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 255, 255, 255)), Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(text);

            tile.Child = g;
            tile.ToolTip = p.Date.HasValue ? $"{p.ShortLabel}\nReleased {p.Date:MMMM yyyy}" : p.ShortLabel;
            tile.MouseLeftButtonUp += (_, _) => OpenDetail(p);
            tile.MouseRightButtonUp += (_, e) => { e.Handled = true; OpenSeasonMenu(tile, p); };
            return tile;
        }
    }
}
