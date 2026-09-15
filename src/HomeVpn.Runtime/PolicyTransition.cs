using HomeVpn.Infrastructure;
using HomeVpn.Models;
namespace HomeVpn.Services;
public static class PolicyTransition
{
    public static async Task<IReadOnlyDictionary<Guid,string>> ApplyAsync(IReadOnlyList<ProfilePlan> plans, ITunnelController controller)
    {
        var errors = new Dictionary<Guid,string>();
        bool blocked = false;
        foreach (var p in plans.Where(x => x.Profile.Backend == TunnelBackendKind.EmbeddedWireGuard))
        {
            try
            {
                if (!p.ShouldRun || p.Profile.RoutingMode != RoutingMode.HomeOnly) await controller.StopAsync(p.Profile.HomeServiceName);
                if (!p.ShouldRun || p.Profile.RoutingMode != RoutingMode.FullTunnel) await controller.StopAsync(p.Profile.FullServiceName);
            }
            catch { blocked = true; errors[p.Profile.Id] = "Der Tunnel konnte nicht vollständig gestoppt werden. Erneut versuchen."; }
        }
        foreach (var p in plans.Where(x => x.ShouldRun))
        {
            if (blocked) { errors[p.Profile.Id] = "Ein anderer Tunnel wird noch beendet. Der gewünschte Zustand bleibt gespeichert."; continue; }
            try
            {
                var name = p.Profile.RoutingMode == RoutingMode.HomeOnly ? p.Profile.HomeServiceName : p.Profile.FullServiceName;
                if (!controller.Query(name).IsRunning) await controller.StartAsync(name);
            }
            catch { errors[p.Profile.Id] = "Verbindung konnte nicht gestartet werden. Installation und Service-Rechte prüfen."; }
        }
        return errors;
    }
}
