using System.Windows;
using System.Windows.Input;

namespace R6ThrowbackLauncher
{
    public partial class EditUsernameDialog : Window
    {
        public string NewUsername { get; private set; } = "";

        public EditUsernameDialog(string seasonName)
        {
            InitializeComponent();
            TitleText.Text = $"Edit Username — {seasonName}";
            UsernameInput.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            NewUsername = UsernameInput.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void UsernameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { NewUsername = UsernameInput.Text.Trim(); DialogResult = true; }
            else if (e.Key == Key.Escape) DialogResult = false;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
