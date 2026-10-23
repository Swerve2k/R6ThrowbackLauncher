using System;
using System.IO;
using System.Windows;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class App : Application
    {
        private static string CrashLog => Path.Combine(AppPaths.DataDir, "crash.log");

        private void App_Startup(object sender, StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                LogCrash(args.Exception);
                args.Handled = true;
                try
                {
                    ThemedDialog.Alert(MainWindow, "Something went wrong",
                        $"{args.Exception.Message}\n\nDetails were saved to {CrashLog}.");
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex) LogCrash(ex);
            };

            var splash = new SplashWindow();
            splash.Show();

            var main = new MainWindow();
            MainWindow = main;
            splash.SetMainWindow(main);

            // Show the main window now so its Loaded event fires; the splash stays on top until it fades.
            main.Show();
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.AppendAllText(CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{AppInfo.Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
