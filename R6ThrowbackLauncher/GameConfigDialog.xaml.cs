using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class GameConfigDialog : Window
    {
        private readonly string _filePath;
        private bool _isDirty;

        public GameConfigDialog(string filePath, string seasonName)
        {
            InitializeComponent();
            _filePath = filePath;

            TitleLabel.Text    = $"Game settings, {seasonName}";
            FilePathLabel.Text = filePath;

            LoadFile();

            ConfigTextBox.TextChanged += (_, _) =>
            {
                _isDirty = true;
                SaveStatusLabel.Text = "";
            };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void LoadFile()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    ConfigTextBox.Text   = File.ReadAllText(_filePath);
                    FileStatusLabel.Text = $"Last changed {File.GetLastWriteTime(_filePath):MMM d, yyyy h:mm tt}";
                }
                else
                {
                    ConfigTextBox.Text   = "; GameSettings.ini was not found.\r\n" +
                                           "; Launch this season once to generate it, or write values here and save to create it.\r\n";
                    FileStatusLabel.Text = "Not created yet";
                }
                _isDirty = false;
            }
            catch (Exception ex)
            {
                ConfigTextBox.Text = $"; Could not read the file:\r\n; {ex.Message}";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, ConfigTextBox.Text);
                _isDirty = false;
                SaveStatusLabel.Text = "Saved";
                FileStatusLabel.Text = $"Last changed {DateTime.Now:MMM d, yyyy h:mm tt}";
            }
            catch (Exception ex)
            {
                ThemedDialog.Alert(this, "Could not save", ex.Message);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isDirty && !ThemedDialog.Confirm(this, "Discard changes?", "You have unsaved changes.", "Discard", "Keep editing"))
                return;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);

        private void OpenInNotepad_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_filePath))
            {
                ThemedDialog.Alert(this, "File not found", "The settings file doesn't exist yet. Launch this season once to create it.");
                return;
            }
            System.Diagnostics.Process.Start("notepad.exe", $"\"{_filePath}\"");
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Cancel_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }
    }
}
