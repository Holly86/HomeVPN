using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using HomeVpn.Core;
using HomeVpn.Models;
using HomeVpn.Services;

namespace HomeVpn.Views;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<ExcludedNetworkRule> _rules;
    private readonly List<VpnProfile> _profiles;

    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 20;
        Height = Math.Min(Height, MaxHeight);
        _services = services;
        _profiles = System.Text.Json.JsonSerializer.Deserialize<List<VpnProfile>>(System.Text.Json.JsonSerializer.Serialize(services.Settings.Profiles))!;
        _rules = new ObservableCollection<ExcludedNetworkRule>(_services.Settings.ExcludedNetworks.Select(x => x.Clone()));
        RulesGrid.ItemsSource = _rules;
        AutostartCheck.IsChecked = _services.Settings.StartWithWindows;
        _services.Settings.NormalizeProfileSelection();
        PrimaryProfileBox.ItemsSource = _profiles;
        PrimaryProfileBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _services.Settings.PrimaryProfileId);
        PrimaryProfileBox.IsEnabled = _services.Settings.Profiles.Count > 0;
        RuleScope.ItemsSource = new[] { new VpnProfile { Id = Guid.Empty, DisplayName = "Alle Verbindungen" } }.Concat(_services.Settings.Profiles).ToArray();
    }

    private async void AddCurrent_Click(object sender, RoutedEventArgs e)
    {
        var network = await Task.Run(() => _services.NetworkDetector.GetSnapshot());
        if (!network.HasUsableNetwork)
        {
            ValidationText.Text = "Aktuell ist kein nutzbares LAN/WLAN erkannt.";
            return;
        }

        var subnet = network.Interfaces
            .SelectMany(x => x.NetworkCidrs)
            .FirstOrDefault(x => Cidr.TryParse(x, out var parsed) && parsed is not null && parsed.Network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

        _rules.Add(new ExcludedNetworkRule
        {
            Name = network.IsWifi ? network.WifiSsid ?? "WLAN" : network.DisplayName,
            NetworkNamePattern = network.IsWifi ? network.WifiSsid : null,
            SubnetCidr = subnet,
            AllowManualOverride = true
        });
        RulesGrid.SelectedIndex = _rules.Count - 1;
        RulesGrid.ScrollIntoView(RulesGrid.SelectedItem);
        ValidationText.Text = string.Empty;
    }

    private void AddBlank_Click(object sender, RoutedEventArgs e)
    {
        _rules.Add(new ExcludedNetworkRule { Name = "Neues Netzwerk", AllowManualOverride = true });
        RulesGrid.SelectedIndex = _rules.Count - 1;
        RulesGrid.ScrollIntoView(RulesGrid.SelectedItem);
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is ExcludedNetworkRule rule)
            _rules.Remove(rule);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        FocusManager.SetFocusedElement(this, this);
        ValidationText.Text = string.Empty;

        foreach (var rule in _rules)
        {
            if (string.IsNullOrWhiteSpace(rule.NetworkNamePattern) && string.IsNullOrWhiteSpace(rule.SubnetCidr))
            {
                ValidationText.Text = $"Regel „{rule.Name}“ benötigt mindestens Netzwerkname/SSID oder Subnetz.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(rule.SubnetCidr) && !Cidr.TryParse(rule.SubnetCidr, out _))
            {
                ValidationText.Text = $"Ungültiges CIDR in Regel „{rule.Name}“: {rule.SubnetCidr}";
                return;
            }
        }

        if (_profiles.Any(p => string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Length > 80 || p.DisplayName.Any(char.IsControl)))
        { ValidationText.Text = "Ein Verbindungsname muss 1 bis 80 Zeichen enthalten."; return; }
        await _services.PolicyEngine.SuspendAsync();
        try
        {
        foreach (var edited in _profiles)
        {
            var original = _services.Settings.Profiles.FirstOrDefault(p => p.Id == edited.Id);
            if (original is not null) original.DisplayName = edited.DisplayName.Trim();
        }
        _services.Settings.ExcludedNetworks = _rules.Select(x => x.Clone()).ToList();
        _services.Settings.StartWithWindows = AutostartCheck.IsChecked == true;
        if (PrimaryProfileBox.SelectedItem is VpnProfile primary)
            _services.Settings.PrimaryProfileId = primary.Id;
        _services.SettingsStore.Save(_services.Settings);

        try
        {
            _services.Autostart.SetEnabled(_services.Settings.StartWithWindows, _services.Installation.InstalledExecutablePath);
        }
        catch (Exception ex)
        {
            ValidationText.Text = $"Autostart konnte nicht geändert werden: {ex.Message}";
            return;
        }

        DialogResult = true;
        }
        finally { _services.PolicyEngine.SetSuspended(false); await _services.PolicyEngine.RefreshAsync(force: true); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Rule_Selected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RuleScope.ItemsSource is IEnumerable<VpnProfile> items && RulesGrid.SelectedItem is ExcludedNetworkRule rule)
            RuleScope.SelectedItem = items.FirstOrDefault(x => x.Id == rule.ProfileIds.FirstOrDefault());
    }
    private void Scope_Selected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    { if (RulesGrid.SelectedItem is ExcludedNetworkRule rule && RuleScope.SelectedItem is VpnProfile p) rule.ProfileIds = p.Id == Guid.Empty ? [] : [p.Id]; }
    private void Import_Click(object sender, RoutedEventArgs e) { DialogResult = false; if (Owner is MainWindow main) main.Dispatcher.BeginInvoke(main.AddProfile); }
    private async void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (PrimaryProfileBox.SelectedItem is not VpnProfile profile) return;
        await _services.PolicyEngine.SuspendAsync();
        try
        {
            if (profile.Backend == TunnelBackendKind.EmbeddedWireGuard) await _services.ProfileInstaller.RemoveAsync(profile.Id);
            _services.Settings.Profiles.RemoveAll(p => p.Id == profile.Id);
            _profiles.Remove(profile);
            _services.Settings.NormalizeProfileSelection();
            _services.SettingsStore.Save(_services.Settings);
            PrimaryProfileBox.Items.Refresh();
            PrimaryProfileBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _services.Settings.PrimaryProfileId);
        }
        catch (Exception ex) { ErrorDialog.Show(this, ex); }
        finally { _services.PolicyEngine.SetSuspended(false); }
    }

    private void ConfigureDns_Click(object sender, RoutedEventArgs e)
    {
        if (PrimaryProfileBox.SelectedItem is not VpnProfile edited) return;
        var original = _services.Settings.Profiles.Single(p => p.Id == edited.Id);
        if (original.Backend != TunnelBackendKind.EmbeddedWireGuard) { ValidationText.Text = "Bitte das alte Profil zunächst als Embedded-Verbindung neu importieren."; return; }
        var dialog = new SplitDnsWindow(edited.DisplayName, original.HomeCidrs, original.SplitDns, async dns =>
        {
            await _services.PolicyEngine.SuspendAsync();
            try
            {
                await _services.ServiceManager.StopAsync(original.HomeServiceName);
                await _services.ServiceManager.StopAsync(original.FullServiceName);
                await _services.ProfileInstaller.ConfigureDnsAsync(original.Id, dns);
                original.SplitDns = dns; edited.SplitDns = dns;
                _services.SettingsStore.Save(_services.Settings);
            }
            finally { _services.PolicyEngine.SetSuspended(false); await _services.PolicyEngine.RefreshAsync(force: true); }
        }) { Owner = this };
        dialog.ShowDialog();
    }
}
