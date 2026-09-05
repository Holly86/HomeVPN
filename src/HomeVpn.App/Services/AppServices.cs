using HomeVpn.Infrastructure;
using HomeVpn.Models;

namespace HomeVpn.Services;

public sealed class AppServices
{
    public required AppSettings Settings { get; init; }
    public required SettingsStore SettingsStore { get; init; }
    public required InstallationService Installation { get; init; }
    public required AutostartService Autostart { get; init; }
    public required NetworkDetector NetworkDetector { get; init; }
    public required WindowsServiceManager ServiceManager { get; init; }
    public required ProfileInstaller ProfileInstaller { get; init; }
    public required VpnPolicyEngine PolicyEngine { get; init; }
}
