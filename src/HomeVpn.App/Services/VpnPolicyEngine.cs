using System.Net.NetworkInformation;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
namespace HomeVpn.Services;

public sealed class VpnPolicyEngine : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly NetworkDetector _network;
    private readonly ITunnelController _controller;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<Guid,string> _overrides = [];
    private System.Threading.Timer? _timer;
    private string? _fingerprint, _recommended;
    private volatile bool _suspended, _disposed;
    public RuntimeState CurrentState { get; private set; } = new();
    public event EventHandler<RuntimeState>? StateChanged;
    public event EventHandler<RuntimeState>? RecommendationRaised;
    public VpnPolicyEngine(AppSettings settings, SettingsStore store, NetworkDetector network, ITunnelController controller)
    { _settings=settings; _store=store; _network=network; _controller=controller; }
    public void Start()
    {
        NetworkChange.NetworkAddressChanged += Changed;
        NetworkChange.NetworkAvailabilityChanged += Availability;
        _timer = new(_ => _ = RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }
    public void SetSuspended(bool value) => _suspended = value;
    public async Task SuspendAsync() { _suspended = true; await _gate.WaitAsync(); _gate.Release(); }
    private NetworkSnapshot Snapshot()
    {
        var network = _network.GetSnapshot();
        if (_fingerprint != network.Fingerprint) { _overrides.Clear(); _fingerprint = network.Fingerprint; }
        return network;
    }
    private Task CommandAsync(Action<NetworkSnapshot> action) => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try { action(Snapshot()); _store.Save(_settings); } finally { _gate.Release(); }
        await RefreshAsync(true);
    });
    private VpnProfile Profile(Guid? id) => _settings.Profiles.First(x => x.Id == (id ?? _settings.SelectedProfileId));
    public Task SelectProfileAsync(Guid id) => CommandAsync(_ => _settings.SelectedProfileId = id);
    public Task ConnectAsync(bool allowExcludedNetworkOverride, Guid? profileId = null) => CommandAsync(n =>
    { var p = Profile(profileId); p.DesiredVpnEnabled = true; if (allowExcludedNetworkOverride) _overrides[p.Id] = n.Fingerprint; });
    public Task DisconnectAsync(Guid? profileId = null) => CommandAsync(_ => { var p = Profile(profileId); p.DesiredVpnEnabled = false; _overrides.Remove(p.Id); });
    public Task SetRoutingModeAsync(RoutingMode mode, Guid? profileId = null) => CommandAsync(_ => Profile(profileId).RoutingMode = mode);
    public Task RefreshAsync(bool force = false) => Task.Run(async () =>
    {
        if (_disposed || _suspended || !await _gate.WaitAsync(force ? Timeout.InfiniteTimeSpan : TimeSpan.Zero)) return;
        try
        {
            if (_suspended || _disposed) return;
            _settings.NormalizeProfileSelection();
            var network = Snapshot();
            var plans = PolicyPlanner.Evaluate(_settings, network, _overrides);
            var errors = new Dictionary<Guid,string>();
            var names = _settings.Profiles.Where(x => x.Backend == TunnelBackendKind.EmbeddedWireGuard).SelectMany(x => new[] { TunnelIdentity.Name(x.Id, RoutingMode.HomeOnly), TunnelIdentity.Name(x.Id, RoutingMode.FullTunnel) }).ToHashSet();
            bool foreign = NetworkInterface.GetAllNetworkInterfaces().Any(x => x.OperationalStatus == OperationalStatus.Up && x.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) && !names.Contains(x.Name));
            if (foreign) plans = plans.Select(p => p.ShouldRun ? p with { ShouldRun = false, Reason = PolicyReason.RouteConflict } : p).ToArray();
            foreach (var error in await PolicyTransition.ApplyAsync(plans, _controller)) errors[error.Key] = error.Value;
            var states = plans.Select(p =>
            {
                ServiceSnapshot? home = null, full = null;
                if (p.Profile.Backend == TunnelBackendKind.EmbeddedWireGuard)
                    try { home = _controller.Query(p.Profile.HomeServiceName); full = _controller.Query(p.Profile.FullServiceName); } catch { errors[p.Profile.Id] = "Service-Status nicht verfügbar. Zugriffsrechte prüfen."; }
                return new ProfileRuntimeState { Profile=p.Profile, Exclusion=p.Exclusion, Reason=p.Reason, DesiredEnabled=p.Profile.DesiredVpnEnabled,
                    EffectiveEnabled=(p.Profile.RoutingMode == RoutingMode.HomeOnly ? home : full)?.IsRunning == true, ManualOverrideActive=p.OverrideActive,
                    CanManualOverride=p.Exclusion?.Rule.AllowManualOverride == true, HomeService=home, FullService=full,
                    Error=errors.GetValueOrDefault(p.Profile.Id) ?? (p.Reason == PolicyReason.MigrationRequired ? "Dieses ältere Profil bitte erneut importieren. Bestehende WireGuard-Dienste bleiben unverändert." : null) };
            }).ToArray();
            var selected = states.FirstOrDefault(x => x.Profile.Id == _settings.SelectedProfileId);
            var recommendation = network.HasUsableNetwork && network.IsWifi && selected?.Exclusion is null ? network.IsOpenWifi ? RecommendationSeverity.Warning : selected?.DesiredEnabled == false ? RecommendationSeverity.Info : RecommendationSeverity.None : RecommendationSeverity.None;
            CurrentState = new RuntimeState { Network=network, Profiles=states, SelectedProfileId=_settings.SelectedProfileId, PrimaryProfileId=_settings.PrimaryProfileId, Recommendation=recommendation };
            StateChanged?.Invoke(this, CurrentState);
            if (recommendation != RecommendationSeverity.None && _recommended != network.Fingerprint) { _recommended=network.Fingerprint; RecommendationRaised?.Invoke(this, CurrentState); }
        }
        catch { /* transient network enumeration failures retry next tick */ }
        finally { _gate.Release(); }
    });
    private void Changed(object? sender, EventArgs e) => _ = RefreshAsync();
    private void Availability(object? sender, NetworkAvailabilityEventArgs e) => _ = RefreshAsync();
    public void Dispose() { _disposed=true; _timer?.Dispose(); NetworkChange.NetworkAddressChanged-=Changed; NetworkChange.NetworkAvailabilityChanged-=Availability; }
}
