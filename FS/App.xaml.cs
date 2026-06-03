using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace BetterFileSys
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Hook up global exception handlers
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            ShowCrashDialog(e.Exception);
            e.Handled = true; // Prevent default Windows crash reporting
            Shutdown(1);
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash(ex);
                ShowCrashDialog(ex);
            }
            // App domain exceptions are fatal; Windows will terminate the app
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            e.SetObserved(); // Prevent background tasks from crashing the app
        }

        private void LogCrash(Exception ex)
        {
            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), "VaultRecon_Crash.log");
                string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FATAL EXCEPTION:\n" +
                                    $"Message: {ex.Message}\n" +
                                    $"Source: {ex.Source}\n" +
                                    $"StackTrace:\n{ex.StackTrace}\n";
                if (ex.InnerException != null)
                {
                    logMessage += $"Inner Exception:\n{ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n";
                }
                logMessage += new string('-', 80) + "\n";
                File.AppendAllText(logPath, logMessage);
            }
            catch { }
        }

        private void ShowCrashDialog(Exception ex)
        {
            MessageBox.Show(
                $"A fatal error occurred in VaultRecon:\n\n{ex.Message}\n\n" +
                $"A crash report has been saved to your Temp folder (VaultRecon_Crash.log).\n" +
                $"The application will now close.",
                "VaultRecon - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
