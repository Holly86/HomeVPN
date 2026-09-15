using System.Windows;
using System.Windows.Controls;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
using HomeVpn.Services;
namespace HomeVpn.Views;
public sealed class SetupResultWindow : Window
{
    private readonly TextBlock _result = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,16,0,16), LineHeight=25 };
    public SetupResultWindow(VpnProfile profile, AppServices services)
    {
        Title="Verbindung eingerichtet · HomeVPN"; Width=520; SizeToContent=SizeToContent.Height; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new TextBlock { Text=profile.DisplayName + " ist eingerichtet", FontSize=23, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(_result); Render(services.ProfileInstaller.LastTest);
        var retry = new Button { Content="Verbindung erneut testen", Padding=new Thickness(12,8,12,8) };
        retry.Click += async (_,_) => { retry.IsEnabled=false; _result.Text="Nur Heimnetz wird geprüft …"; try { var response=await services.ProfileInstaller.TestAsync(profile.Id); Render(response.Test); } catch(Exception ex) { ErrorDialog.Show(this,ex); } finally { retry.IsEnabled=true; } };
        panel.Children.Add(retry);
        var done = new Button { Content="Fertig", IsDefault=true, Margin=new Thickness(0,10,0,0), Padding=new Thickness(12,8,12,8) };
        done.Click += (_,_) => Close(); panel.Children.Add(done); Content=panel;
    }
    private void Render(TunnelTestResult? t)
    {
        static string Result(bool value) => value ? "Erfolgreich" : "Nicht bestätigt";
        _result.Text=t is null ? "Noch kein Verbindungstest verfügbar." : $"Runtime: {Result(t.Runtime)}\nTunnelservice: {Result(t.Service)}\nAdapter: {Result(t.Adapter)}\nZielrouten: {Result(t.Routes)}\nPeer-Handshake: {(t.Handshake == true ? "Erfolgreich" : "Nicht bestätigt")}\n\n{t.Summary}\n\nDie Verbindung bleibt bis zur Aktivierung ausgeschaltet.";
    }
}
