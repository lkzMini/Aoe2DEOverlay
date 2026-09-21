using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Aoe2DEOverlay;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "AoE2MinimalOverlay", out var createdNew);
        if (!createdNew) { Shutdown(); return; }
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppLogger.Info("AoE2 Minimal Overlay starting");
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled UI error", e.Exception);
        e.Handled = true;
    }
}
