using System.Threading;
using System.Windows;
using HomeVpn.Infrastructure;
using HomeVpn.Services;
using HomeVpn.Views;

namespace HomeVpn;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showEventCts;
    private TrayService? _tray;
    private AppServices? _services;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = e.Args;
        var adminIndex = Array.FindIndex(args, x => x.Equals("--admin-install", StringComparison.OrdinalIgnoreCase));
        if (adminIndex >= 0)
        {
            if (adminIndex + 1 >= args.Length)
            {
                Shutdown(2);
                return;
            }

            var exitCode = await AdminInstaller.RunAsync(args[adminIndex + 1]);
            Shutdown(exitCode);
            return;
        }

        var installation = new InstallationService();
        try
        {
            if (installation.EnsureInstalledAndRestartIfNeeded(args))
            {
                Shutdown(0);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Home VPN konnte nicht in das Benutzerprofil installiert werden.\n\n{ex.Message}",
                "Home VPN",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _mutex = new Mutex(true, @"Local\HomeVPN.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HomeVPN.ShowWindow");
        if (!createdNew)
        {
            _showEvent.Set();
            Shutdown(0);
            return;
        }

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        var autostart = new AutostartService();
        var networkDetector = new NetworkDetector();
        var serviceManager = new WindowsServiceManager();
        var profileInstaller = new ProfileInstaller(settingsStore, installation);
        var policyEngine = new VpnPolicyEngine(settings, settingsStore, networkDetector, serviceManager);

        _services = new AppServices
        {
            Settings = settings,
            SettingsStore = settingsStore,
            Installation = installation,
            Autostart = autostart,
            NetworkDetector = networkDetector,
            ServiceManager = serviceManager,
            ProfileInstaller = profileInstaller,
            PolicyEngine = policyEngine
        };

        try
        {
            autostart.SetEnabled(settings.StartWithWindows, installation.InstalledExecutablePath);
        }
        catch
        {
            // The setting can be corrected from the settings window later.
        }

        _mainWindow = new MainWindow(_services);
        MainWindow = _mainWindow;
        _tray = new TrayService(_mainWindow, _services);
        policyEngine.Start();

        var background = args.Any(x => x.Equals("--background", StringComparison.OrdinalIgnoreCase));
        if (!background || settings.Profiles.Count == 0)
            _mainWindow.Show();

        _showEventCts = new CancellationTokenSource();
        _ = ListenForShowRequestsAsync(_showEvent, _showEventCts.Token);
    }

    private async Task ListenForShowRequestsAsync(EventWaitHandle showEvent, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = await Task.Run(() => showEvent.WaitOne(500), cancellationToken);
            if (signaled && _mainWindow is not null)
                await Dispatcher.InvokeAsync(_mainWindow.ShowFromTray);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _mainWindow?.PrepareForSystemShutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEventCts?.Cancel();
        _tray?.Dispose();
        _services?.PolicyEngine.Dispose();
        _showEvent?.Dispose();
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
