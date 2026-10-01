using System.Windows;
using System.ComponentModel;

namespace OpenLimiter.App;

public partial class App : System.Windows.Application
{
    private MainWindow? mainWindow;
    private TrayIconController? trayIcon;
    private bool isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mainWindow = new MainWindow();
        mainWindow.Closing += MainWindow_Closing;
        mainWindow.StateChanged += MainWindow_StateChanged;
        trayIcon = new TrayIconController(ShowMainWindow, RequestExit);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (isExiting)
        {
            return;
        }

        e.Cancel = true;
        mainWindow?.Hide();
        trayIcon?.ShowWindowHiddenMessage();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (mainWindow?.WindowState == WindowState.Minimized)
        {
            mainWindow.Hide();
            trayIcon?.ShowWindowHiddenMessage();
        }
    }

    private void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void RequestExit()
    {
        isExiting = true;
        mainWindow?.Close();
        Shutdown();
    }
}
