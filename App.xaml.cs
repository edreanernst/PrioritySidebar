using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PrioritySidebar;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one sidebar at a time; a second launch just exits.
        _singleInstance = new Mutex(true, @"Local\PrioritySidebar", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the sidebar running rather than crash and leave the reserved strip behind.
        Log(e.Exception);
        e.Handled = true;
    }

    public static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Storage.Folder);
            File.AppendAllText(Path.Combine(Storage.Folder, "errors.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
