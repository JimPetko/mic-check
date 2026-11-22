using System.Threading;
using System.Windows;

namespace MicCheck;

public partial class App : Application
{
    // Only one instance may own the mic and the virtual cable; a second launch just shows the first.
    private const string InstanceName = "MicCheck.SingleInstance";
    private const string ShowEventName = "MicCheck.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out var isFirstInstance);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirstInstance)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.InvokeAsync(() =>
            {
                // A second launch can land while we're shutting down; Show() on a closed window throws.
                if (!window.IsClosed)
                    window.ShowFromTray();
            }), null, Timeout.Infinite, executeOnlyOnce: false);

        // Started at sign-in: stay in the tray; the window is fully set up and just not shown.
        if (!e.Args.Contains(StartupRegistration.TrayArgument))
            window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
