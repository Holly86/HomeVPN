using System.Windows;
using HomeVpn.Core;
using HomeVpn.Models;

namespace HomeVpn.Views;

public partial class ImportProfileWindow : Window
{
    public string ProfileDisplayName { get; private set; } = "Home";
    public string TunnelName { get; private set; } = "Home";
    public IReadOnlyList<string> HomeCidrs { get; private set; } = [];
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
        SourceText.Text = $"Quelle: {sourceFileName}. Die importierte .conf-Datei selbst wird nicht in den App-Einstellungen gespeichert.";
        DisplayNameBox.Text = defaultTunnelName;
        TunnelNameBox.Text = defaultTunnelName;
        HomeCidrsBox.Text = string.Join(Environment.NewLine, homeCidrCandidates);
        CurrentNetworkText.Text = currentNetwork.HasUsableNetwork
            ? $"Aktuelles Netzwerk: {currentNetwork.DisplayName} · {string.Join(", ", currentNetwork.LocalNetworks)}"
            : "Aktuell wurde kein nutzbares physisches Netzwerk erkannt.";

        HomeExclusionCheck.IsChecked = CurrentNetworkMatches(homeCidrCandidates, currentNetwork);
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

        var invalid = cidrs.FirstOrDefault(x => !Cidr.TryParse(x, out _));
        if (invalid is not null)
        {
            ValidationText.Text = $"Ungültiges CIDR: {invalid}";
            return;
        }

        if (string.IsNullOrWhiteSpace(TunnelNameBox.Text))
        {
            ValidationText.Text = "Bitte einen technischen Tunnelnamen angeben.";
            return;
        }

        ProfileDisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? TunnelNameBox.Text.Trim() : DisplayNameBox.Text.Trim();
        TunnelName = TunnelNameBox.Text.Trim();
        HomeCidrs = cidrs;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

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
