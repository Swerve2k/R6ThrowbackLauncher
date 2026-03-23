using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace R6ThrowbackLauncher
{
    public partial class DetectedInstallsDialog : Window
    {
        public List<string> SelectedFolders { get; private set; } = new();

        public DetectedInstallsDialog(List<string> folders)
        {
            InitializeComponent();

            foreach (var folder in folders)
            {
                var row = new Border
                {
                    Background   = new SolidColorBrush(Color.FromRgb(21, 21, 31)),
                    CornerRadius = new CornerRadius(6),
                    Padding      = new Thickness(14, 10, 14, 10),
                    Margin       = new Thickness(0, 0, 0, 8)
                };

                var cb = new CheckBox
                {
                    IsChecked                = true,
                    Tag                      = folder,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                cb.Checked   += (_, __) => UpdateCount();
                cb.Unchecked += (_, __) => UpdateCount();

                var labelStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                labelStack.Children.Add(new TextBlock
                {
                    Text         = Path.GetFileName(folder),
                    Foreground   = Brushes.White,
                    FontSize     = 14,
                    FontWeight   = FontWeights.SemiBold
                });
                labelStack.Children.Add(new TextBlock
                {
                    Text         = folder,
                    Foreground   = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                    FontSize     = 11,
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
                });

                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(cb);
                sp.Children.Add(labelStack);
                row.Child = sp;
                FolderListPanel.Children.Add(row);
            }

            UpdateCount();
        }

        private void UpdateCount()
        {
            int count = GetCheckBoxes().Count(cb => cb.IsChecked == true);
            int total = GetCheckBoxes().Count();
            CountLabel.Text = $"{count} of {total} selected";
            ImportButton.Content = $"IMPORT SELECTED ({count})";
        }

        private IEnumerable<CheckBox> GetCheckBoxes() =>
            FolderListPanel.Children
                .OfType<Border>()
                .SelectMany(b => ((StackPanel)b.Child).Children.OfType<CheckBox>());

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cb in GetCheckBoxes()) cb.IsChecked = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cb in GetCheckBoxes()) cb.IsChecked = false;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            SelectedFolders = GetCheckBoxes()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => (string)cb.Tag)
                .ToList();
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
