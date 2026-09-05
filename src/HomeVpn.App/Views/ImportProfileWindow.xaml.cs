using System.Windows;
using HomeVpn.Core;
using HomeVpn.Models;

namespace HomeVpn.Views;

public partial class ImportProfileWindow : Window
{
    public string ProfileDisplayName { get; private set; } = "Home";
    public string TunnelName { get; private set; } = "Home";
    public IReadOnlyList<string> HomeCidrs { get; private set; } = [];
    public SplitDnsSettings SplitDnsSettings { get; private set; } = new();
    public bool CreateHomeExclusion => HomeExclusionCheck.IsChecked == true;
    public bool StartWithWindows => AutostartCheck.IsChecked == true;
    public bool MakePrimary => MakePrimaryCheck.IsChecked == true;

    public ImportProfileWindow(
        string sourceFileName,
        string defaultTunnelName,
        IReadOnlyList<string> homeCidrCandidates,
        NetworkSnapshot currentNetwork,
        bool hasExistingProfiles)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        Height = Math.Min(Height, MaxHeight);
        SourceText.Text = $"Quelle: {sourceFileName}. Die importierte .conf-Datei selbst wird nicht in den App-Einstellungen gespeichert.";
        DisplayNameBox.Text = defaultTunnelName;
        TunnelNameBox.Text = defaultTunnelName;
        HomeCidrsBox.Text = string.Join(Environment.NewLine, homeCidrCandidates);
        CurrentNetworkText.Text = currentNetwork.HasUsableNetwork
            ? $"Aktuelles Netzwerk: {currentNetwork.DisplayName} · {string.Join(", ", currentNetwork.LocalNetworks)}"
            : "Aktuell wurde kein nutzbares physisches Netzwerk erkannt.";

        HomeExclusionCheck.IsChecked = false;
        MakePrimaryCheck.IsChecked = !hasExistingProfiles;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        var cidrs = HomeCidrsBox.Text
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cidrs.Length == 0)
        {
            ValidationText.Text = "Mindestens ein Zielnetz-CIDR ist für den Modus „Nur Heimnetz“ erforderlich.";
            return;
        }

        var invalid = cidrs.FirstOrDefault(x => !Cidr.TryParse(x, out var parsed) || parsed!.PrefixLength <= 1);
        if (invalid is not null)
        {
            ValidationText.Text = "Bitte konkrete Zielnetze eingeben. /0 und /1 sind für Nur Heimnetz nicht zulässig.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) || DisplayNameBox.Text.Any(char.IsControl))
        {
            ValidationText.Text = "Bitte einen Verbindungsnamen angeben.";
            return;
        }

        ProfileDisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? TunnelNameBox.Text.Trim() : DisplayNameBox.Text.Trim();
        TunnelName = TunnelNameBox.Text.Trim();
        HomeCidrs = cidrs;
        try { SplitDnsSettings = HomeVpn.Infrastructure.SplitDns.Normalize(SplitDnsSettings, cidrs); }
        catch (InvalidDataException ex) { ValidationText.Text = ex.Message; return; }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ConfigureDns_Click(object sender, RoutedEventArgs e)
    {
        var routes = HomeCidrsBox.Text.Split(['\r', '\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var dialog = new SplitDnsWindow(DisplayNameBox.Text, routes, SplitDnsSettings) { Owner = this };
        if (dialog.ShowDialog() == true) SplitDnsSettings = dialog.Settings;
    }
    private void Next_Click(object sender, RoutedEventArgs e) => Steps.SelectedIndex = Math.Min(3, Steps.SelectedIndex + 1);

    private static bool CurrentNetworkMatches(IEnumerable<string> cidrs, NetworkSnapshot network)
    {
        foreach (var text in cidrs)
        {
            if (!Cidr.TryParse(text, out var cidr) || cidr is null)
                continue;
            if (network.LocalAddresses.Any(cidr.Contains))
                return true;
        }
        return false;
    }
}
