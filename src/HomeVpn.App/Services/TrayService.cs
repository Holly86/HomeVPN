using HomeVpn.Models;
using HomeVpn.Views;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace HomeVpn.Services;

public sealed class TrayService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly AppServices _services;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _profileMenu;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Forms.ToolStripMenuItem _homeOnlyItem;
    private readonly Forms.ToolStripMenuItem _fullTunnelItem;

    public TrayService(MainWindow mainWindow, AppServices services)
    {
        _mainWindow = mainWindow;
        _services = services;

        _statusItem = new Forms.ToolStripMenuItem("Home VPN") { Enabled = false };
        _profileMenu = new Forms.ToolStripMenuItem("Verbindung");

        _toggleItem = new Forms.ToolStripMenuItem("VPN einschalten");
        _toggleItem.Click += async (_, _) => await ToggleAsync();

        _homeOnlyItem = new Forms.ToolStripMenuItem("Nur Heimnetz");
        _homeOnlyItem.Click += async (_, _) => await _services.PolicyEngine.SetRoutingModeAsync(RoutingMode.HomeOnly);
        _fullTunnelItem = new Forms.ToolStripMenuItem("Gesamter Verkehr");
        _fullTunnelItem.Click += async (_, _) => await _services.PolicyEngine.SetRoutingModeAsync(RoutingMode.FullTunnel);

        var openItem = new Forms.ToolStripMenuItem("Home VPN öffnen");
        openItem.Click += (_, _) => ShowMainWindow();
        var settingsItem = new Forms.ToolStripMenuItem("Einstellungen");
        settingsItem.Click += (_, _) => _mainWindow.OpenSettings();
        var exitItem = new Forms.ToolStripMenuItem("Beenden");
        exitItem.Click += (_, _) => _mainWindow.ExitApplication();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_profileMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_homeOnlyItem);
        menu.Items.Add(_fullTunnelItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Shield,
            Text = "Home VPN",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => ShowMainWindow();

        _services.PolicyEngine.StateChanged += OnStateChanged;
        _services.PolicyEngine.RecommendationRaised += OnRecommendationRaised;
        Update(_services.PolicyEngine.CurrentState);
    }

    private async Task ToggleAsync()
    {
        var state = _services.PolicyEngine.CurrentState;
        if (state.EffectiveEnabled)
        {
            await _services.PolicyEngine.DisconnectAsync();
            return;
        }

        var overrideExcluded = state.Exclusion?.Rule.AllowManualOverride == true;
        await _services.PolicyEngine.ConnectAsync(overrideExcluded);
    }

    private void OnStateChanged(object? sender, RuntimeState state)
    {
        if (_mainWindow.Dispatcher.CheckAccess())
            Update(state);
        else
            _mainWindow.Dispatcher.BeginInvoke(() => Update(state));
    }

    private void Update(RuntimeState state)
    {
        var profile = state.SelectedProfile?.Profile ?? _services.Settings.GetSelectedProfile();
        var name = profile?.DisplayName ?? "Kein Profil";

        RebuildProfileMenu(profile?.Id);

        _statusItem.Text = state.EffectiveEnabled
            ? $"{name} · verbunden · {(state.RoutingMode == RoutingMode.HomeOnly ? "Nur Heimnetz" : "Gesamter Verkehr")}" 
            : state.Reason == PolicyReason.ExcludedNetwork
                ? $"{name} · pausiert · {state.Exclusion?.Rule.Name}"
                : $"{name} · ausgeschaltet";

        _toggleItem.Text = state.EffectiveEnabled ? $"{name} ausschalten" :
            state.Reason == PolicyReason.ExcludedNetwork && state.CanManualOverride ? "Trotzdem verbinden" : $"{name} einschalten";
        _toggleItem.Enabled = profile is not null &&
                              state.Reason != PolicyReason.RouteConflict &&
                              !(state.Reason == PolicyReason.ExcludedNetwork && !state.CanManualOverride);

        _homeOnlyItem.Checked = state.RoutingMode == RoutingMode.HomeOnly;
        _fullTunnelItem.Checked = state.RoutingMode == RoutingMode.FullTunnel;
        _homeOnlyItem.Enabled = profile is not null;
        _fullTunnelItem.Enabled = profile is not null;

        var tip = state.EffectiveEnabled ? $"Home VPN – {name} verbunden" : $"Home VPN – {name} aus";
        _notifyIcon.Text = tip.Length <= 63 ? tip : tip[..63];
    }

    private void RebuildProfileMenu(Guid? selectedProfileId)
    {
        _profileMenu.DropDownItems.Clear();
        _profileMenu.Enabled = _services.Settings.Profiles.Count > 0;
        if (_services.Settings.Profiles.Count == 0)
        {
            _profileMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Kein Profil") { Enabled = false });
            return;
        }

        foreach (var profile in _services.Settings.Profiles)
        {
            var isPrimary = profile.Id == _services.Settings.PrimaryProfileId;
            var item = new Forms.ToolStripMenuItem(isPrimary ? $"{profile.DisplayName} (Standard)" : profile.DisplayName)
            {
                Checked = profile.Id == selectedProfileId,
                Tag = profile.Id
            };
            item.Click += async (_, _) =>
            {
                if (item.Tag is Guid id)
                {
                    await _services.PolicyEngine.SelectProfileAsync(id);
                    ShowMainWindow();
                }
            };
            _profileMenu.DropDownItems.Add(item);
        }
    }

    private void OnRecommendationRaised(object? sender, RuntimeState state)
    {
        _mainWindow.Dispatcher.BeginInvoke(() =>
        {
            var profileName = state.SelectedProfile?.Profile.DisplayName ?? "VPN";
            _notifyIcon.BalloonTipTitle = state.Network.IsOpenWifi
                ? "Offenes WLAN erkannt"
                : "Externes WLAN erkannt";
            _notifyIcon.BalloonTipText = state.Network.IsOpenWifi
                ? $"Dieses WLAN ist unverschlüsselt. Die Verwendung von „{profileName}“ wird empfohlen."
                : $"Für dieses externe WLAN wird die Verwendung von „{profileName}“ empfohlen.";
            _notifyIcon.BalloonTipIcon = state.Network.IsOpenWifi
                ? Forms.ToolTipIcon.Warning
                : Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(5000);
        });
    }

    private void ShowMainWindow() => _mainWindow.ShowFromTray();

    public void Dispose()
    {
        _services.PolicyEngine.StateChanged -= OnStateChanged;
        _services.PolicyEngine.RecommendationRaised -= OnRecommendationRaised;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
