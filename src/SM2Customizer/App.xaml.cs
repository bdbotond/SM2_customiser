using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SM2Customizer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogAndShowError("Fatal Domain Error", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogAndShowError("Unhandled Dispatcher Error", args.Exception);
                args.Handled = true;
            };
        }

        private static void LogAndShowError(string title, Exception? ex)
        {
            string msg = ex?.ToString() ?? "Unknown error";
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt"), msg);
            }
            catch { }
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
