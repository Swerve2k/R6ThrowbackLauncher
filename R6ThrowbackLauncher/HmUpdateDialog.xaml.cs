using System.Windows;

namespace R6ThrowbackLauncher
{
    public partial class HmUpdateDialog : Window
    {
        public enum UpdateResult { UpdateNow, Later, Skip }

        public UpdateResult Result { get; private set; } = UpdateResult.Later;

        public HmUpdateDialog(string currentVersion, string newVersion, string notes)
        {
            InitializeComponent();

            CurrentVersionText.Text = $"Current: v{currentVersion}";
            NewVersionText.Text     = $"New: v{newVersion}";

            if (!string.IsNullOrWhiteSpace(notes))
                NotesText.Text = notes.Replace("\\n", "\n");
        }

        private void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateResult.UpdateNow;
            Close();
        }

        private void Later_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateResult.Later;
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateResult.Skip;
            Close();
        }
    }
}
