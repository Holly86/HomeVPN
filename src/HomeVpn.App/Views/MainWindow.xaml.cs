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
        MaxHeight = SystemParameters.WorkArea.Height - 20;
        Height = Math.Min(Height, MaxHeight);
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
        SizeChanged += (_, _) =>
        {
            // Keep primary actions and a usable dynamic profile list on small laptop work areas.
            bool compact = ActualHeight < 540;
            LayoutRoot.Margin = new Thickness(compact ? 12 : 20);
            BrandHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ProfileCard.Padding = new Thickness(compact ? 8 : 16);
        };
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

            if (!ReferenceEquals(ProfileSelector.ItemsSource, _services.Settings.Profiles))
                ProfileSelector.ItemsSource = _services.Settings.Profiles;
            ProfileSelector.SelectedItem = profile;
            ProfileSelector.IsEnabled = _services.Settings.Profiles.Count > 0;
            PrimaryProfileChip.Visibility = profile is not null && profile.Id == _services.Settings.PrimaryProfileId
                ? Visibility.Visible
                : Visibility.Collapsed;

            FooterText.Text = profile is null ? "Noch keine Verbindung" : $"{_services.Settings.Profiles.Count} Verbindung(en) · Embedded WireGuard";
            ImportButton.Content = "Verbindung hinzufügen";
            OtherProfiles.ItemsSource = state.Profiles.Where(p => p.Profile.Id != profile?.Id).Select(p => new { p.Profile,
                Summary = $"Gewünscht: {(p.DesiredEnabled ? "Ein" : "Aus")} · Effektiv: {(p.EffectiveEnabled ? "Verbunden" : "Getrennt")} · {(p.RoutingMode == RoutingMode.HomeOnly ? "Nur Heimnetz" : "Gesamter Verkehr")}" + (p.Reason == PolicyReason.RouteConflict ? " · Routing-Konflikt" : p.Reason == PolicyReason.ExcludedNetwork ? " · Netzwerkregel" : ""),
                Action = p.EffectiveEnabled || p.DesiredEnabled && p.Reason != PolicyReason.ExcludedNetwork ? "Trennen" : p.Reason == PolicyReason.ExcludedNetwork ? "Trotzdem verbinden" : "Verbinden" }).ToArray();

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

            if (active?.State is WindowsServiceState.StartPending or WindowsServiceState.StopPending)
            {
                var starting = active.State == WindowsServiceState.StartPending;
                TitleText.Text = starting ? "Verbindung wird aufgebaut …" : "Verbindung wird getrennt …";
                TitleText.Foreground = text;
                SubtitleText.Text = profile.DisplayName;
                StatusBadge.Background = muted;
                StatusBadgeText.Text = "…";
                ConnectionValue.Text = starting ? "Verbindet …" : "Trennt …";
                PrimaryButtonText.Text = "Bitte warten …";
                PrimaryButton.IsEnabled = false;
                return;
            }

            if (state.EffectiveEnabled)
            {
                TitleText.Text = $"{profile.DisplayName} ist verbunden";
                TitleText.Foreground = green;
                SubtitleText.Text = state.ManualOverrideActive
                    ? $"Verbunden mit manuellem Override im Netzwerk „{state.Network.DisplayName}“."
                    : "Der ausgewählte WireGuard-Tunnel ist aktiv.";
                if (profile.SplitDns.Enabled && profile.RoutingMode == RoutingMode.HomeOnly)
                    SubtitleText.Text += " " + SplitDnsRuntime.DisplayStatus(profile.Id) + ".";
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
                    SubtitleText.Text = "Zielnetze oder Heimnetz-DNS-Domänen überschneiden sich, oder ein anderer Tunnel beansprucht den Verkehr. Der gewünschte Zustand bleibt erhalten.";
                    PrimaryButtonText.Text = "Verbindungswunsch ausschalten";
                    PrimaryButton.IsEnabled = true;
                    ShowPolicyBanner(
                        "Routing-Konflikt vermieden",
                        "Andere Verbindung trennen oder überschneidende Zielnetze und DNS-Domänen prüfen. Fremde WireGuard-Verbindungen bleiben unverändert.",
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
            if (state.EffectiveEnabled || state.Reason == PolicyReason.RouteConflict)
                await _services.PolicyEngine.DisconnectAsync();
            else
                await _services.PolicyEngine.ConnectAsync(state.Reason == PolicyReason.ExcludedNetwork && state.CanManualOverride);
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, ex);
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
            ErrorDialog.Show(this, ex);
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
            ErrorDialog.Show(this, ex);
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
            ErrorDialog.Show(this, ex);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e) => await ImportConfigurationAsync();
    public async void AddProfile() => await ImportConfigurationAsync();

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
            config = await Task.Run(() => WireGuardConfig.Parse(dialog.FileName));
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, ex);
            return;
        }

        var network = await Task.Run(() => _services.NetworkDetector.GetSnapshot());
        var candidates = config.DetectHomeCidrs().ToList();

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

        await _services.PolicyEngine.SuspendAsync();
        ImportButton.IsEnabled = false;
        PrimaryButton.IsEnabled = false;
        SubtitleText.Text = "Verbindung wird eingerichtet und geprüft …";
        try
        {
            var profile = await _services.ProfileInstaller.InstallAsync(
                config,
                importWindow.ProfileDisplayName,
                importWindow.TunnelName,
                importWindow.HomeCidrs,
                oldProfile: null, splitDns: importWindow.SplitDnsSettings);

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

            new SetupResultWindow(profile, _services) { Owner = this }.ShowDialog();
        }
        catch (OperationCanceledException ex)
        {
            ErrorDialog.Show(this, ex);
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, ex);
        }
        finally
        {
            ImportButton.IsEnabled = true;
            PrimaryButton.IsEnabled = true;
            _services.PolicyEngine.SetSuspended(false);
            await _services.PolicyEngine.RefreshAsync(force: true);
        }
    }

    private string GetUniqueDefaultName(string sourceName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "VPN" : sourceName;
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
        var subnet = network.LocalNetworks.FirstOrDefault(x => Cidr.TryParse(x, out var c) && c!.Network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        if (!network.HasUsableNetwork || subnet is null) return;
        _services.Settings.ExcludedNetworks.Add(new ExcludedNetworkRule { Name = "Zuhause · " + profile.DisplayName,
            NetworkNamePattern = network.IsWifi ? network.WifiSsid : null, SubnetCidr = subnet, AllowManualOverride = true, ProfileIds = [profile.Id] });
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private async void OtherSelect_Click(object sender, RoutedEventArgs e)
    {
        try { if (sender is Button { Tag: Guid id }) await _services.PolicyEngine.SelectProfileAsync(id); }
        catch (Exception ex) { ErrorDialog.Show(this, ex); }
    }
    private async void OtherToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } button) return;
        button.IsEnabled = false;
        try {
            var state = _services.PolicyEngine.CurrentState.Profiles.First(x => x.Profile.Id == id);
            if (state.EffectiveEnabled || state.DesiredEnabled && state.Reason != PolicyReason.ExcludedNetwork) await _services.PolicyEngine.DisconnectAsync(id);
            else await _services.PolicyEngine.ConnectAsync(state.CanManualOverride, id);
        } catch (Exception ex) { ErrorDialog.Show(this, ex); }
        finally { button.IsEnabled = true; }
    }
}
