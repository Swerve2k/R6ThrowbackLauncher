using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class DetectedInstallsDialog : Window
    {
        private readonly List<CheckBox> _boxes = new();

        public List<string> SelectedFolders { get; private set; } = new();

        public DetectedInstallsDialog(List<string> folders)
        {
            InitializeComponent();

            foreach (var folder in folders)
            {
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) };
                row.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
                row.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");

                var label = new StackPanel();
                label.Children.Add(UiFactory.Text(Path.GetFileName(folder), 14, "Brush.Fg", FontWeights.SemiBold));
                label.Children.Add(UiFactory.Text(folder, 11, "Brush.Fg3"));

                var cb = new CheckBox { IsChecked = true, Tag = folder, Content = label };
                cb.Styled("Check");
                cb.Checked += (_, _) => UpdateCount();
                cb.Unchecked += (_, _) => UpdateCount();
                _boxes.Add(cb);

                row.Child = cb;
                FolderListPanel.Children.Add(row);
            }

            UpdateCount();
        }

        private void UpdateCount()
        {
            int count = _boxes.Count(cb => cb.IsChecked == true);
            CountLabel.Text = $"{count} of {_boxes.Count} selected";
            ImportButton.Content = $"Import selected ({count})";
            ImportButton.IsEnabled = count > 0;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cb in _boxes) cb.IsChecked = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cb in _boxes) cb.IsChecked = false;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            SelectedFolders = _boxes.Where(cb => cb.IsChecked == true).Select(cb => (string)cb.Tag).ToList();
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
