using System.Collections.ObjectModel;
using System.Windows;
using HomeVpn.Core;
using HomeVpn.Models;
using HomeVpn.Services;

namespace HomeVpn.Views;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<ExcludedNetworkRule> _rules;

    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _rules = new ObservableCollection<ExcludedNetworkRule>(_services.Settings.ExcludedNetworks.Select(x => x.Clone()));
        RulesGrid.ItemsSource = _rules;
        AutostartCheck.IsChecked = _services.Settings.StartWithWindows;
        _services.Settings.NormalizeProfileSelection();
        PrimaryProfileBox.ItemsSource = _services.Settings.Profiles;
        PrimaryProfileBox.SelectedItem = _services.Settings.GetPrimaryProfile();
        PrimaryProfileBox.IsEnabled = _services.Settings.Profiles.Count > 0;
    }

    private void AddCurrent_Click(object sender, RoutedEventArgs e)
    {
        var network = _services.NetworkDetector.GetSnapshot();
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
        RulesGrid.CommitEdit();
        RulesGrid.CommitEdit();
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

        await _services.PolicyEngine.RefreshAsync(force: true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
