using System.Windows;
using System.Windows.Controls;
namespace HomeVpn.Views;
public static class ErrorDialog
{
    public static void Show(Window owner, Exception error)
    {
        var dialog = new Window { Owner=owner, Title="HomeVPN", Width=460, SizeToContent=SizeToContent.Height, WindowStartupLocation=WindowStartupLocation.CenterOwner, ResizeMode=ResizeMode.NoResize };
        var panel = new StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new TextBlock { Text="HomeVPN konnte den Vorgang nicht abschließen.", FontSize=19, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text=error is System.ComponentModel.Win32Exception ? "Windows hat den Vorgang nicht freigegeben. Administratorfreigabe oder Service-Rechte prüfen." : error.Message, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,12) });
        panel.Children.Add(new Expander { Header="Technische Details", Content=new TextBlock { Text=error.GetType().Name + " · " + error.HResult.ToString("X8"), Margin=new Thickness(8), TextWrapping=TextWrapping.Wrap } });
        var close = new Button { Content="Schließen und erneut versuchen", Margin=new Thickness(0,18,0,0), Padding=new Thickness(12,8,12,8), IsDefault=true };
        close.Click += (_,_) => dialog.Close(); panel.Children.Add(close); dialog.Content=panel; dialog.ShowDialog();
    }
}
