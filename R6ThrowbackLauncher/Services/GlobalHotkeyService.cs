using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>
    /// Installs a system-wide (low-level) keyboard hook to detect Shift+F2
    /// even when the game window has focus.
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        // ── Win32 ─────────────────────────────────────────────────────────────
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int VK_F2          = 0x71;
        private const int VK_SHIFT       = 0x10;

        // ── State ─────────────────────────────────────────────────────────────
        private IntPtr                _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;              // must stay alive — never let GC collect
        private bool                  _disposed;

        /// <summary>Fired on the thread-pool when Shift+F2 is pressed while game is running.</summary>
        public event Action? OverlayToggleRequested;

        // ── Public API ────────────────────────────────────────────────────────

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc  = HookCallback;
            using var process = Process.GetCurrentProcess();
            using var module  = process.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        }

        public void Stop()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        // ── Hook callback ─────────────────────────────────────────────────────

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int  vkCode    = Marshal.ReadInt32(lParam);
                bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                if (vkCode == VK_F2 && shiftDown)
                {
                    OverlayToggleRequested?.Invoke();
                    return (IntPtr)1;   // swallow — don't pass Shift+F2 to the game
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
