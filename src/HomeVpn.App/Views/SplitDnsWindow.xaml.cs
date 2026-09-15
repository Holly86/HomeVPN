using System.Windows;
using HomeVpn.Infrastructure;
using HomeVpn.Models;

namespace HomeVpn.Views;

public partial class SplitDnsWindow : Window
{
    private readonly IReadOnlyList<string> _routes;
    private readonly Func<SplitDnsSettings, Task>? _apply;
    public SplitDnsSettings Settings { get; private set; } = new();
    public SplitDnsWindow(string name, IReadOnlyList<string> routes, SplitDnsSettings settings, Func<SplitDnsSettings, Task>? apply = null)
    {
        InitializeComponent();
        _routes = routes; _apply = apply;
        MaxHeight = SystemParameters.WorkArea.Height - 20; Height = Math.Min(Height, MaxHeight);
        ProfileText.Text = name;
        ServerBox.Text = settings.Server; DomainsBox.Text = string.Join(Environment.NewLine, settings.Domains);
        ApplyHint.Text = apply is null ? "Die Einstellung wird beim Import eingerichtet." : "Übernehmen speichert diese DNS-Einstellung sofort. Die Verbindung wird kurz getrennt; Windows fragt einmalig nach Administratorrechten.";
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var domains = DomainsBox.Text.Split(['\r', '\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            Settings = SplitDns.Normalize(new() { Server = ServerBox.Text, Domains = string.IsNullOrWhiteSpace(ServerBox.Text) ? [] : domains }, _routes);
            SaveButton.IsEnabled = false; IsEnabled = false;
            if (_apply is not null) await _apply(Settings);
            DialogResult = true;
        }
        catch (Exception ex) { ValidationText.Text = ex is InvalidDataException ? ex.Message : "DNS-Einstellung konnte nicht gespeichert werden. Administratorbestätigung und Windows-DNS-Richtlinien prüfen."; }
        finally { IsEnabled = true; SaveButton.IsEnabled = true; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
