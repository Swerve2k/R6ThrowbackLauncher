using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace R6ThrowbackLauncher
{
    public partial class InGameToast : Window
    {
        public const int DisplayDurationMs = 4500;

        public enum ToastType { FriendOnline, FriendInGame, NewMessage }

        public InGameToast(string title, string subtitle, ToastType type)
        {
            InitializeComponent();

            TitleText.Text    = title;
            SubtitleText.Text = subtitle;
            IconText.Text = type switch
            {
                ToastType.FriendOnline => "",
                ToastType.FriendInGame => "",
                _                      => "",
            };

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(DisplayDurationMs);
            FadeOut();
        }

        public void FadeOut()
        {
            var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(280)));
            anim.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, anim);
        }
    }
}
