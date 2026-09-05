using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
using HomeVpn.Services;
using HomeVpn.Views;
namespace HomeVpn.VisualHarness;

public static class Program
{
    [STAThread] public static void Main() { var app=new Harness(); app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/HomeVPN;component/ProductStyles.xaml") }); app.Run(); }
}
// Test-only executable. Not in installer/CI artifacts. Never connects to SCM or contains keys.
public sealed class Harness : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode=ShutdownMode.OnMainWindowClose;
        var profiles=new List<VpnProfile> { new() { DisplayName="Zuhause",Backend=TunnelBackendKind.EmbeddedWireGuard,HomeCidrs=["10.10.0.0/24"] },new() { DisplayName="Mutter",Backend=TunnelBackendKind.EmbeddedWireGuard,HomeCidrs=["10.20.0.0/24"] },new() { DisplayName="Vater",Backend=TunnelBackendKind.EmbeddedWireGuard,HomeCidrs=["10.30.0.0/24"] } };
        var settings=new AppSettings { Profiles=profiles,PrimaryProfileId=profiles[0].Id,SelectedProfileId=profiles[0].Id,StartWithWindows=false };
        if (e.Args.Contains("--many"))
        {
            profiles[0].DisplayName="Zuhause und Ferienhaus – gemeinsame Verbindung für Familie und Heimlabor";
            profiles.AddRange(Enumerable.Range(4,20).Select(i=>new VpnProfile { DisplayName=$"Labor {i} – zusätzliche Verbindung",Backend=TunnelBackendKind.EmbeddedWireGuard,HomeCidrs=[$"10.{i}.0.0/24"] }));
        }
        var store=new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"HomeVPN-visual-fixture"));
        var network=new NetworkDetector(); var controller=new FakeController(); var installation=new InstallationService();
        var policy=new VpnPolicyEngine(settings,store,network,controller); policy.SetSuspended(true);
        var services=new AppServices { Settings=settings,SettingsStore=store,NetworkDetector=network,ServiceManager=new WindowsServiceManager(),Installation=installation,Autostart=new AutostartService(),ProfileInstaller=new ProfileInstaller(installation),PolicyEngine=policy };
        var window=new MainWindow(services) { Title="HomeVPN · VISUAL FIXTURE",Height=620 };
        if (e.Args.Contains("--laptop")) { window.Width=900; window.Height=480; }
        MainWindow=window;
        var toolbar=new Window { Title="HomeVPN visual scenarios",Width=850,SizeToContent=SizeToContent.Height };
        var buttons=new WrapPanel { Margin=new Thickness(8) };
        foreach(var scenario in new[] {"Disconnected","Connecting","Connected","Excluded","Override","Open Wi-Fi","Conflict","Missing service","Access denied"})
        {
            var button=new Button { Content=scenario,Margin=new Thickness(4),Padding=new Thickness(8) };
            button.Click+=(_,_)=>ShowScenario(scenario,profiles,window); buttons.Children.Add(button);
        }
        foreach(var scale in new[] {1.0,1.25,1.5})
        {
            var button=new Button {Content=$"Scale {scale}",Margin=new Thickness(4)};
            button.Click+=(_,_)=> { ((FrameworkElement)window.Content).LayoutTransform=new System.Windows.Media.ScaleTransform(scale,scale); window.Width= Math.Min(1366,SystemParameters.WorkArea.Width); window.Height=Math.Min(728,SystemParameters.WorkArea.Height-20); };
            buttons.Children.Add(button);
        }
        toolbar.Content=buttons;
        var errorButton=new Button { Content="Error dialog",Margin=new Thickness(4) };
        errorButton.Click+=(_,_)=>ErrorDialog.Show(window,new InvalidDataException("Die WireGuard-Konfiguration ist ungültig. Bitte prüfen Sie die exportierte Datei und versuchen Sie den Import erneut."));
        buttons.Children.Add(errorButton);
        var dnsButton=new Button { Content="Split-DNS dialog",Margin=new Thickness(4) };
        dnsButton.Click+=(_,_)=>new SplitDnsWindow("Zuhause · DNS-Ansicht mit Beispieldaten",["10.10.0.0/24"],new() { Server="10.10.0.53",Domains=["home.arpa","fritz.box"] }) { Owner=window }.ShowDialog();
        buttons.Children.Add(dnsButton);
        window.Loaded+=(_,_)=>ShowScenario("Disconnected",profiles,window);
        window.Show(); toolbar.Show();
    }
    private static void ShowScenario(string scenario,List<VpnProfile> profiles,MainWindow window)
    {
        bool connected=scenario is "Connected" or "Override";
        var network=new NetworkSnapshot { Fingerprint="fixture",HasUsableNetwork=true,DisplayName=scenario=="Excluded" ? "Office" : "Hotel-WLAN",IsWifi=true,IsOpenWifi=scenario=="Open Wi-Fi" };
        var states=profiles.Select((p,i)=>new ProfileRuntimeState { Profile=p,DesiredEnabled=(i==0 && scenario!="Disconnected") || (connected && i==2),EffectiveEnabled=connected && (i==0 || i==2),Reason=i==0 ? scenario switch {"Excluded"=>PolicyReason.ExcludedNetwork,"Override"=>PolicyReason.ManualOverride,"Conflict"=>PolicyReason.RouteConflict,_=>PolicyReason.Normal} : PolicyReason.UserOff,
            Exclusion=scenario is "Excluded" or "Override" ? new() {Rule=new() {Name="Office"}} : null,CanManualOverride=true,ManualOverrideActive=scenario=="Override",
            HomeService=new() {Name=p.HomeServiceName,State=scenario=="Connecting" ? WindowsServiceState.StartPending : connected ? WindowsServiceState.Running : scenario=="Missing service" ? WindowsServiceState.NotFound : WindowsServiceState.Stopped,ProcessStartedAt=DateTimeOffset.Now.AddMinutes(-13)},
            Error=scenario=="Access denied" ? "Service-Status nicht verfügbar. Zugriffsrechte prüfen." : scenario=="Missing service" ? "Tunnelservice fehlt. Verbindung erneut einrichten." : null }).ToArray();
        var state=new RuntimeState { Network=network,Profiles=states,SelectedProfileId=profiles[0].Id,PrimaryProfileId=profiles[0].Id,Recommendation=scenario=="Open Wi-Fi" ? RecommendationSeverity.Warning : RecommendationSeverity.None };
        typeof(MainWindow).GetMethod("UpdateUi",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[state]);
    }
    private sealed class FakeController : ITunnelController
    {
        public ServiceSnapshot Query(string n)=>new(){Name=n,State=WindowsServiceState.Stopped};
        public Task StartAsync(string n,CancellationToken c=default)=>Task.CompletedTask;
        public Task StopAsync(string n,CancellationToken c=default)=>Task.CompletedTask;
    }
}
