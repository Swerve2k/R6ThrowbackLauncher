using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace R6ThrowbackLauncher
{
    public partial class SplashWindow : Window
    {
        private static SplashWindow? _current;

        public static SplashWindow? Current => _current;

        private readonly DispatcherTimer _minTimer;
        private bool _mainReady    = false;
        private bool _minElapsed   = false;
        private MainWindow? _main;

        public SplashWindow()
        {
            InitializeComponent();
            _current = this;
            VersionText.Text = $"v{Services.AppInfo.Version}";

            // Keep the splash up briefly so it doesn't flash.
            _minTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            _minTimer.Tick += (_, _) =>
            {
                _minTimer.Stop();
                _minElapsed = true;
                TryFinish();
            };
            _minTimer.Start();
        }

        /// <summary>Called from App_Startup to hand over the MainWindow reference.</summary>
        public void SetMainWindow(MainWindow main)
        {
            _main = main;
            _main.Loaded += (_, _) =>
            {
                _mainReady = true;
                TryFinish();
            };
        }

        /// <summary>Update the loading status text.</summary>
        public void UpdateStatus(string text)
        {
            Dispatcher.Invoke(() => { if (StatusText != null) StatusText.Text = text; });
        }

        private void TryFinish()
        {
            if (!_mainReady || !_minElapsed) return;
            Dispatcher.Invoke(DoFinish);
        }

        private void DoFinish()
        {
            // MainWindow is already visible — just fade the splash out
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) =>
            {
                _current = null;
                Close();
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
