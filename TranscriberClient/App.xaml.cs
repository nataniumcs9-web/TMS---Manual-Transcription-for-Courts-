using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using Serilog;
using TranscriberClient.Views;

namespace TranscriberClient;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppSettings.Initialize();

        var logDirectory = Path.GetDirectoryName(AppSettings.LogFile);
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(AppSettings.LogFile, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            Directory.CreateDirectory(AppSettings.DocsFolder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not create the transcription documents folder {DocumentsFolder}", AppSettings.DocsFolder);
            MessageBox.Show(
                $"Could not create the transcription documents folder:\n{AppSettings.DocsFolder}\n\n{ex.Message}",
                "Documents Folder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error((Exception)args.ExceptionObject, "Unhandled exception");
        };

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var loginWindow = new LoginWindow();
        MainWindow = loginWindow;
        var splashWindow = new SplashWindow();
        splashWindow.Show();

        await Task.Delay(TimeSpan.FromMilliseconds(2200));

        loginWindow.Show();
        splashWindow.Close();
    }
}
