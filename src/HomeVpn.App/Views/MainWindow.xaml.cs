using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HomeVpn.Core;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
using HomeVpn.Services;
using Microsoft.Win32;

namespace HomeVpn.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private bool _allowClose;
    private bool _updatingUi;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _services.Settings.NormalizeProfileSelection();
        ProfileSelector.ItemsSource = _services.Settings.Profiles;
        _services.PolicyEngine.StateChanged += PolicyEngine_StateChanged;
        Loaded += async (_, _) =>
        {
            UpdateUi(_services.PolicyEngine.CurrentState);
            await _services.PolicyEngine.RefreshAsync(force: true);
        };
        Closing += MainWindow_Closing;
    }

    public void ShowFromTray()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ExitApplication()
    {
        _allowClose = true;
        Close();
        Application.Current.Shutdown();
    }

    public void PrepareForSystemShutdown() => _allowClose = true;

    public void OpenSettings()
    {
        Dispatcher.Invoke(() =>
        {
            ShowFromTray();
            var window = new SettingsWindow(_services) { Owner = this };
            if (window.ShowDialog() == true)
            {
                ProfileSelector.Items.Refresh();
                UpdateUi(_services.PolicyEngine.CurrentState);
            }
        });
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        Hide();
    }

    private void PolicyEngine_StateChanged(object? sender, RuntimeState state)
    {
        if (Dispatcher.CheckAccess())
            UpdateUi(state);
        else
            Dispatcher.BeginInvoke(() => UpdateUi(state));
    }

    private void UpdateUi(RuntimeState state)
    {
        _updatingUi = true;
        try
        {
            _services.Settings.NormalizeProfileSelection();
            var profileState = state.SelectedProfile;
            var profile = profileState?.Profile ?? _services.Settings.GetSelectedProfile();
            var green = (Brush)FindResource("GreenBrush");
            var red = (Brush)FindResource("RedBrush");
            var text = (Brush)FindResource("TextBrush");
            var muted = (Brush)FindResource("MutedBrush");

            ProfileSelector.ItemsSource = null;
            ProfileSelector.ItemsSource = _services.Settings.Profiles;
            ProfileSelector.SelectedItem = profile;
            ProfileSelector.IsEnabled = _services.Settings.Profiles.Count > 0;
            PrimaryProfileChip.Visibility = profile is not null && profile.Id == _services.Settings.PrimaryProfileId
                ? Visibility.Visible
                : Visibility.Collapsed;

            FooterText.Text = profile is null
                ? "Kein VPN-Profil importiert"
                : $"{_services.Settings.Profiles.Count} Profil(e) · {profile.HomeTunnelName} / {profile.FullTunnelName}";
            ImportButton.Content = profile is null ? "VPN-Konfiguration importieren" : "Weitere Verbindung importieren";

            NetworkValue.Text = state.Network.DisplayName;
            RoutingValue.Text = state.RoutingMode == RoutingMode.HomeOnly ? "Nur Heimnetz" : "Gesamter Verkehr";
            HomeOnlyRadio.IsChecked = state.RoutingMode == RoutingMode.HomeOnly;
            FullTunnelRadio.IsChecked = state.RoutingMode == RoutingMode.FullTunnel;
            HomeOnlyRadio.IsEnabled = profile is not null;
            FullTunnelRadio.IsEnabled = profile is not null;

            var active = state.ActiveService;
            TunnelValue.Text = active is null
                ? "Nicht eingerichtet"
                : active.State switch
                {
                    WindowsServiceState.Running => $"WireGuard · {profile?.DisplayName}",
                    WindowsServiceState.StartPending => "WireGuard · verbindet …",
                    WindowsServiceState.StopPending => "WireGuard · trennt …",
                    WindowsServiceState.NotFound => "Dienst fehlt",
                    _ => "WireGuard"
                };
            DurationValue.Text = FormatDuration(active);

            PolicyBanner.Visibility = Visibility.Collapsed;
            OverrideButton.Visibility = Visibility.Collapsed;
            ErrorBanner.Visibility = string.IsNullOrWhiteSpace(state.Error) ? Visibility.Collapsed : Visibility.Visible;
            ErrorText.Text = state.Error ?? string.Empty;

            if (profile is null)
            {
                TitleText.Text = "VPN-Konfiguration erforderlich";
                TitleText.Foreground = text;
                SubtitleText.Text = "Importieren Sie eine WireGuard-Konfiguration. Jede Verbindung erhält einen frei wählbaren Namen; Schlüsselmaterial bleibt ausschließlich auf diesem PC.";
                StatusBadge.Background = red;
                StatusBadgeText.Text = "×";
                ConnectionValue.Text = "Nicht eingerichtet";
                ConnectionValue.Foreground = muted;
                PrimaryButtonText.Text = "VPN-Konfiguration importieren";
                PrimaryButton.IsEnabled = true;
                return;
            }

            if (state.EffectiveEnabled)
            {
                TitleText.Text = $"{profile.DisplayName} ist verbunden";
                TitleText.Foreground = green;
                SubtitleText.Text = state.ManualOverrideActive
                    ? $"Verbunden mit manuellem Override im Netzwerk „{state.Network.DisplayName}“."
                    : "Der ausgewählte WireGuard-Tunnel ist aktiv.";
                StatusBadge.Background = green;
                StatusBadgeText.Text = "✓";
                ConnectionValue.Text = "Verbunden";
                ConnectionValue.Foreground = green;
                PrimaryButtonText.Text = $"{profile.DisplayName} ausschalten";
                PrimaryButton.IsEnabled = true;

                if (state.ManualOverrideActive && state.Exclusion is not null)
                {
                    ShowPolicyBanner(
                        "Ausschlussregel manuell übersteuert",
                        $"„{state.Exclusion.Rule.Name}“ würde den automatischen VPN-Aufbau normalerweise aussetzen. Der Override gilt nur für diese Netzwerksitzung.",
                        showOverrideButton: false);
                }
                return;
            }

            StatusBadge.Background = red;
            StatusBadgeText.Text = "×";
            ConnectionValue.Text = "Getrennt";
            ConnectionValue.Foreground = red;

            switch (state.Reason)
            {
                case PolicyReason.ExcludedNetwork:
                    TitleText.Text = $"{profile.DisplayName} ist pausiert";
                    TitleText.Foreground = text;
                    SubtitleText.Text = $"„{state.Exclusion?.Rule.Name}“ wurde erkannt. Die App baut diese Verbindung hier nicht automatisch auf.";
                    PrimaryButtonText.Text = state.CanManualOverride ? "Trotzdem verbinden" : "VPN in diesem Netzwerk blockiert";
                    PrimaryButton.IsEnabled = state.CanManualOverride;
                    ShowPolicyBanner(
                        "Ausgeschlossenes Netzwerk erkannt",
                        state.CanManualOverride
                            ? "Der automatische VPN-Aufbau ist pausiert. Sie können die Regel für die aktuelle Netzwerksitzung bewusst übersteuern."
                            : "Für dieses Netzwerk ist kein manueller Override erlaubt.",
                        state.CanManualOverride);
                    break;

                case PolicyReason.NoNetwork:
                    TitleText.Text = "Warten auf Netzwerk";
                    TitleText.Foreground = text;
                    SubtitleText.Text = "Sobald ein nutzbares LAN oder WLAN verfügbar ist, wird die gespeicherte VPN-Policy erneut ausgewertet.";
                    PrimaryButtonText.Text = $"{profile.DisplayName} einschalten";
                    PrimaryButton.IsEnabled = false;
                    break;

                case PolicyReason.RouteConflict:
                    TitleText.Text = $"{profile.DisplayName} wartet";
                    TitleText.Foreground = text;
                    SubtitleText.Text = "Eine andere Full-Tunnel-Verbindung ist aktiv. Mehrere Home-only-Tunnel dürfen parallel laufen; Full-Tunnel wird exklusiv behandelt.";
                    PrimaryButtonText.Text = "Andere Full-Tunnel-Verbindung zuerst trennen";
                    PrimaryButton.IsEnabled = false;
                    ShowPolicyBanner(
                        "Routing-Konflikt vermieden",
                        "Home VPN verhindert konkurrierende /0-Routen und mehrere gleichzeitige Kill-Switch-Full-Tunnel.",
                        showOverrideButton: false);
                    break;

                default:
                    TitleText.Text = $"{profile.DisplayName} ist ausgeschaltet";
                    TitleText.Foreground = text;
                    SubtitleText.Text = "Keine Verbindung dieses Profils ist aktiv.";
                    PrimaryButtonText.Text = $"{profile.DisplayName} einschalten";
                    PrimaryButton.IsEnabled = state.Network.HasUsableNetwork;
                    break;
            }

            if (state.Recommendation != RecommendationSeverity.None)
            {
                ShowPolicyBanner(
                    state.Network.IsOpenWifi ? "Offenes WLAN erkannt" : "Externes WLAN erkannt",
                    state.Network.IsOpenWifi
                        ? $"Dieses WLAN meldet keine WLAN-Verschlüsselung. Die Verwendung von „{profile.DisplayName}“ wird ausdrücklich empfohlen."
                        : $"Dieses WLAN ist nicht als ausgeschlossenes bzw. vertrautes Netzwerk hinterlegt. Die Verwendung von „{profile.DisplayName}“ wird empfohlen.",
                    showOverrideButton: false);
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void ShowPolicyBanner(string title, string message, bool showOverrideButton)
    {
        PolicyBanner.Visibility = Visibility.Visible;
        PolicyBannerTitle.Text = title;
        PolicyBannerText.Text = message;
        OverrideButton.Visibility = showOverrideButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatDuration(ServiceSnapshot? service)
    {
        if (service?.IsRunning != true)
            return "--:--:--";
        if (service.ProcessStartedAt is null)
            return "Aktiv";

        var elapsed = DateTimeOffset.Now - service.ProcessStartedAt.Value;
        if (elapsed < TimeSpan.Zero)
            return "Aktiv";
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_services.Settings.GetSelectedProfile() is null)
        {
            await ImportConfigurationAsync();
            return;
        }

        var state = _services.PolicyEngine.CurrentState;
        try
        {
            PrimaryButton.IsEnabled = false;
            if (state.EffectiveEnabled)
                await _services.PolicyEngine.DisconnectAsync();
            else
                await _services.PolicyEngine.ConnectAsync(state.Reason == PolicyReason.ExcludedNetwork && state.CanManualOverride);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Home VPN", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PrimaryButton.IsEnabled = true;
        }
    }

    private async void OverrideButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _services.PolicyEngine.ConnectAsync(allowExcludedNetworkOverride: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Home VPN", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RoutingRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || _services.Settings.GetSelectedProfile() is null)
            return;

        var mode = FullTunnelRadio.IsChecked == true ? RoutingMode.FullTunnel : RoutingMode.HomeOnly;
        try
        {
            await _services.PolicyEngine.SetRoutingModeAsync(mode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Routing konnte nicht geändert werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || ProfileSelector.SelectedItem is not VpnProfile profile)
            return;

        try
        {
            await _services.PolicyEngine.SelectProfileAsync(profile.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "VPN-Profil konnte nicht ausgewählt werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e) => await ImportConfigurationAsync();

    private async Task ImportConfigurationAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "WireGuard-Konfiguration auswählen",
            Filter = "WireGuard-Konfiguration (*.conf)|*.conf|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        WireGuardConfig config;
        try
        {
            config = WireGuardConfig.Parse(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ungültige WireGuard-Konfiguration", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var network = _services.NetworkDetector.GetSnapshot();
        var candidates = config.DetectHomeCidrs().ToList();
        if (candidates.Count == 0)
            candidates.AddRange(_services.NetworkDetector.GetPrivateIpv4Networks(network));

        var defaultName = GetUniqueDefaultName(Path.GetFileNameWithoutExtension(dialog.FileName));
        var importWindow = new ImportProfileWindow(
            Path.GetFileName(dialog.FileName),
            defaultName,
            candidates,
            network,
            _services.Settings.Profiles.Count > 0)
        {
            Owner = this
        };

        if (importWindow.ShowDialog() != true)
            return;

        var sanitized = WireGuardConfig.SanitizeTunnelName(importWindow.TunnelName);
        var full = WireGuardConfig.SanitizeTunnelName(sanitized + "-Full");
        if (_services.Settings.Profiles.Any(p =>
                p.HomeTunnelName.Equals(sanitized, StringComparison.OrdinalIgnoreCase) ||
                p.FullTunnelName.Equals(sanitized, StringComparison.OrdinalIgnoreCase) ||
                p.HomeTunnelName.Equals(full, StringComparison.OrdinalIgnoreCase) ||
                p.FullTunnelName.Equals(full, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Dieser technische Tunnelname ist bereits vergeben. Bitte wählen Sie beim Import einen anderen Namen.", "Tunnelname bereits vorhanden", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _services.PolicyEngine.SetSuspended(true);
        IsEnabled = false;
        try
        {
            var profile = await _services.ProfileInstaller.InstallAsync(
                config,
                importWindow.ProfileDisplayName,
                importWindow.TunnelName,
                importWindow.HomeCidrs,
                oldProfile: null);

            _services.Settings.Profiles.Add(profile);
            _services.Settings.SelectedProfileId = profile.Id;
            if (_services.Settings.PrimaryProfileId is null || importWindow.MakePrimary)
                _services.Settings.PrimaryProfileId = profile.Id;

            _services.Settings.StartWithWindows = importWindow.StartWithWindows;
            if (importWindow.CreateHomeExclusion)
                AddDetectedHomeExclusion(profile, network);

            _services.SettingsStore.Save(_services.Settings);
            _services.Autostart.SetEnabled(_services.Settings.StartWithWindows, _services.Installation.InstalledExecutablePath);
            ProfileSelector.Items.Refresh();

            MessageBox.Show(
                this,
                $"„{profile.DisplayName}“ wurde installiert. Die Roh-Konfigurationsdatei wird von Home VPN nicht gespeichert.\n\nFür dieses Profil existieren ein Home-only- und ein Full-Tunnel-Dienst. Weitere Profile können unabhängig hinzugefügt und – im Home-only-Modus – parallel aktiviert werden.",
                "VPN-Verbindung eingerichtet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException ex)
        {
            MessageBox.Show(this, ex.Message, "Einrichtung abgebrochen", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Die VPN-Konfiguration konnte nicht installiert werden.\n\n{ex.Message}\n\nFalls MakeMeAdmin verwendet wird: temporäre Administratorrechte aktivieren und den Import erneut starten.",
                "Home VPN",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            _services.PolicyEngine.SetSuspended(false);
            await _services.PolicyEngine.RefreshAsync(force: true);
        }
    }

    private string GetUniqueDefaultName(string sourceName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "Home" : sourceName;
        var candidate = baseName;
        var suffix = 2;
        while (_services.Settings.Profiles.Any(x =>
                   x.DisplayName.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                   x.HomeTunnelName.Equals(WireGuardConfig.SanitizeTunnelName(candidate), StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName}-{suffix++}";
        }
        return candidate;
    }

    private void AddDetectedHomeExclusion(VpnProfile profile, NetworkSnapshot network)
    {
        foreach (var iface in network.Interfaces)
        {
            foreach (var address in iface.Addresses)
            {
                var homeCidr = profile.HomeCidrs
                    .Select(x => Cidr.TryParse(x, out var parsed) ? parsed : null)
                    .FirstOrDefault(x => x is not null && x.Contains(address));
                if (homeCidr is null)
                    continue;

                var localCidr = iface.NetworkCidrs
                    .Select(x => Cidr.TryParse(x, out var parsed) ? parsed : null)
                    .FirstOrDefault(x => x is not null && x.Contains(address))?.ToString();

                if (string.IsNullOrWhiteSpace(localCidr))
                    continue;

                var namePattern = network.IsWifi ? network.WifiSsid : null;
                var alreadyExists = _services.Settings.ExcludedNetworks.Any(r =>
                    r.AppliesTo(profile.Id) &&
                    string.Equals(r.NetworkNamePattern, namePattern, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.SubnetCidr, localCidr, StringComparison.OrdinalIgnoreCase));
                if (alreadyExists)
                    return;

                _services.Settings.ExcludedNetworks.Add(new ExcludedNetworkRule
                {
                    Name = $"Zuhause · {profile.DisplayName}",
                    NetworkNamePattern = namePattern,
                    SubnetCidr = localCidr,
                    AllowManualOverride = true,
                    ProfileIds = [profile.Id]
                });
                return;
            }
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();
}
