using System;
using System.Collections.Generic;
using System.Windows;

namespace R6ThrowbackLauncher
{
    public static class ToastManager
    {
        private const int MarginRight  = 16;
        private const int MarginTop    = 16;
        private const int ToastHeight  = 80;
        private const int ToastGap     = 8;
        private const int ToastWidth   = 360;

        private static readonly List<InGameToast> _active = new();
        private static readonly object            _lock   = new();

        // ── Public helpers ─────────────────────────────────────────────────────

        public static void ShowFriendOnline(string displayName)
            => ShowOnUiThread($"{displayName} is now online", "Swerve2k Downloader", InGameToast.ToastType.FriendOnline);

        public static void ShowFriendInGame(string displayName)
            => ShowOnUiThread($"{displayName} jumped into a game", "Rainbow Six Siege", InGameToast.ToastType.FriendInGame);

        public static void ShowNewMessage(string fromDisplayName, string preview)
        {
            string sub = preview.Length > 48 ? preview[..48] + "..." : preview;
            ShowOnUiThread(fromDisplayName, sub, InGameToast.ToastType.NewMessage);
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private static void ShowOnUiThread(string title, string subtitle, InGameToast.ToastType type)
            => Application.Current?.Dispatcher.Invoke(() => Show(title, subtitle, type));

        private static void Show(string title, string subtitle, InGameToast.ToastType type)
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                         ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

            var toast = new InGameToast(title, subtitle, type);

            double left = screen.Right - ToastWidth  - MarginRight;
            double top  = screen.Top   + MarginTop   + _active.Count * (ToastHeight + ToastGap);

            toast.Left = left;
            toast.Top  = top;

            lock (_lock) { _active.Add(toast); }

            toast.Closed += (_, _) =>
            {
                lock (_lock) { _active.Remove(toast); }
                ReStack(screen);
            };

            toast.Show();
        }

        private static void ReStack(System.Drawing.Rectangle screen)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    for (int i = 0; i < _active.Count; i++)
                        _active[i].Top = screen.Top + MarginTop + i * (ToastHeight + ToastGap);
                }
            });
        }
    }
}
