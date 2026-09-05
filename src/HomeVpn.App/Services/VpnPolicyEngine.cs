using System.Net.NetworkInformation;
using HomeVpn.Core;
using HomeVpn.Infrastructure;
using HomeVpn.Models;

namespace HomeVpn.Services;

public sealed class VpnPolicyEngine : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly NetworkDetector _networkDetector;
    private readonly WindowsServiceManager _serviceManager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, string> _manualOverrideFingerprints = [];
    private System.Threading.Timer? _timer;
    private string? _lastNetworkFingerprint;
    private string? _lastRecommendationFingerprint;
    private bool _disposed;
    private volatile bool _suspended;

    public RuntimeState CurrentState { get; private set; } = new();

    public event EventHandler<RuntimeState>? StateChanged;
    public event EventHandler<RuntimeState>? RecommendationRaised;

    public VpnPolicyEngine(
        AppSettings settings,
        SettingsStore settingsStore,
        NetworkDetector networkDetector,
        WindowsServiceManager serviceManager)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _networkDetector = networkDetector;
        _serviceManager = serviceManager;
        _settings.NormalizeProfileSelection();
    }

    public void Start()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _timer = new System.Threading.Timer(_ => _ = RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void SetSuspended(bool suspended) => _suspended = suspended;

    public async Task SelectProfileAsync(Guid profileId)
    {
        if (_settings.Profiles.All(x => x.Id != profileId))
            throw new InvalidOperationException("VPN profile not found.");

        _settings.SelectedProfileId = profileId;
        _settingsStore.Save(_settings);
        await RefreshAsync(force: true);
    }

    public async Task ConnectAsync(bool allowExcludedNetworkOverride, Guid? profileId = null)
    {
        var profile = ResolveProfile(profileId);
        var snapshot = _networkDetector.GetSnapshot();
        var exclusion = NetworkRuleMatcher.FindMatch(snapshot, _settings.ExcludedNetworks, profile.Id);
        if (exclusion is not null && allowExcludedNetworkOverride && exclusion.Rule.AllowManualOverride)
            _manualOverrideFingerprints[profile.Id] = snapshot.Fingerprint;

        profile.DesiredVpnEnabled = true;
        _settingsStore.Save(_settings);
        await RefreshAsync(force: true);
    }

    public async Task DisconnectAsync(Guid? profileId = null)
    {
        var profile = ResolveProfile(profileId);
        profile.DesiredVpnEnabled = false;
        _manualOverrideFingerprints.Remove(profile.Id);
        _settingsStore.Save(_settings);
        await RefreshAsync(force: true);
    }

    public async Task SetRoutingModeAsync(RoutingMode mode, Guid? profileId = null)
    {
        var profile = ResolveProfile(profileId);
        if (profile.RoutingMode == mode)
            return;

        profile.RoutingMode = mode;
        _settingsStore.Save(_settings);
        await RefreshAsync(force: true);
    }

    public async Task RefreshAsync(bool force = false)
    {
        if (_disposed || _suspended)
            return;

        if (!await _gate.WaitAsync(force ? TimeSpan.FromSeconds(2) : TimeSpan.Zero))
            return;

        try
        {
            _settings.NormalizeProfileSelection();
            var network = _networkDetector.GetSnapshot();
            if (!string.Equals(_lastNetworkFingerprint, network.Fingerprint, StringComparison.Ordinal))
            {
                _manualOverrideFingerprints.Clear();
                _lastNetworkFingerprint = network.Fingerprint;
            }

            if (_settings.Profiles.Count == 0)
            {
                Publish(new RuntimeState
                {
                    Network = network,
                    SelectedProfileId = null,
                    PrimaryProfileId = null,
                    Profiles = []
                });
                return;
            }

            var planned = _settings.Profiles.Select(profile => BuildPlan(profile, network)).ToList();

            // A WireGuard for Windows full-tunnel service uses /0 routing and kill-switch semantics.
            // To avoid ambiguous routing and competing kill switches, only one FullTunnel profile is
            // allowed to be effective at a time. Home-only profiles can run in parallel only when
            // their target CIDRs do not overlap.
            var fullTunnelCandidates = planned
                .Where(x => x.ShouldRun && x.Profile.RoutingMode == RoutingMode.FullTunnel)
                .ToList();

            Guid? fullTunnelOwnerId = null;
            if (fullTunnelCandidates.Count > 0)
            {
                var selected = _settings.SelectedProfileId;
                var primary = _settings.PrimaryProfileId;
                fullTunnelOwnerId = fullTunnelCandidates.FirstOrDefault(x => x.Profile.Id == selected)?.Profile.Id
                                    ?? fullTunnelCandidates.FirstOrDefault(x => x.Profile.Id == primary)?.Profile.Id
                                    ?? fullTunnelCandidates[0].Profile.Id;
            }
            else
            {
                ApplySplitTunnelRouteConflictPolicy(planned);
            }

            var states = new List<ProfileRuntimeState>();
            foreach (var plan in planned)
            {
                if (fullTunnelOwnerId is Guid ownerId && plan.Profile.Id != ownerId && plan.ShouldRun)
                {
                    plan.ShouldRun = false;
                    plan.Reason = PolicyReason.RouteConflict;
                }

                states.Add(await ApplyPlanAsync(plan));
            }

            var selectedState = _settings.SelectedProfileId is Guid selectedId
                ? states.FirstOrDefault(x => x.Profile.Id == selectedId)
                : null;

            var recommendation = RecommendationSeverity.None;
            if (selectedState is not null &&
                !selectedState.DesiredEnabled && network.HasUsableNetwork &&
                selectedState.Exclusion is null && network.IsWifi)
            {
                recommendation = network.IsOpenWifi ? RecommendationSeverity.Warning : RecommendationSeverity.Info;
            }

            var state = new RuntimeState
            {
                Network = network,
                SelectedProfileId = _settings.SelectedProfileId,
                PrimaryProfileId = _settings.PrimaryProfileId,
                Profiles = states,
                Recommendation = recommendation
            };

            Publish(state);

            if (recommendation != RecommendationSeverity.None &&
                !string.Equals(_lastRecommendationFingerprint, network.Fingerprint, StringComparison.Ordinal))
            {
                _lastRecommendationFingerprint = network.Fingerprint;
                RecommendationRaised?.Invoke(this, state);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ApplySplitTunnelRouteConflictPolicy(List<ProfilePlan> planned)
    {
        var selected = _settings.SelectedProfileId;
        var primary = _settings.PrimaryProfileId;

        // Stable ordering makes the user's currently selected profile the winner when two
        // Home-only profiles target the same/overlapping subnet. The Standard-VPN is next.
        var candidates = planned
            .Where(x => x.ShouldRun && x.Profile.RoutingMode == RoutingMode.HomeOnly)
            .OrderByDescending(x => x.Profile.Id == selected)
            .ThenByDescending(x => x.Profile.Id == primary)
            .ToList();

        var accepted = new List<ProfilePlan>();
        foreach (var candidate in candidates)
        {
            if (accepted.Any(other => HomeRoutesOverlap(candidate.Profile, other.Profile)))
            {
                candidate.ShouldRun = false;
                candidate.Reason = PolicyReason.RouteConflict;
                continue;
            }

            accepted.Add(candidate);
        }
    }

    private static bool HomeRoutesOverlap(VpnProfile left, VpnProfile right)
    {
        var leftCidrs = left.HomeCidrs
            .Select(x => Cidr.TryParse(x, out var parsed) ? parsed : null)
            .Where(x => x is not null)
            .Cast<Cidr>()
            .ToArray();
        var rightCidrs = right.HomeCidrs
            .Select(x => Cidr.TryParse(x, out var parsed) ? parsed : null)
            .Where(x => x is not null)
            .Cast<Cidr>()
            .ToArray();

        return leftCidrs.Any(a => rightCidrs.Any(a.Overlaps));
    }

    private ProfilePlan BuildPlan(VpnProfile profile, NetworkSnapshot network)
    {
        var exclusion = NetworkRuleMatcher.FindMatch(network, _settings.ExcludedNetworks, profile.Id);
        var overrideActive = exclusion is not null &&
                             exclusion.Rule.AllowManualOverride &&
                             _manualOverrideFingerprints.TryGetValue(profile.Id, out var fingerprint) &&
                             string.Equals(fingerprint, network.Fingerprint, StringComparison.Ordinal);

        var reason = PolicyReason.Normal;
        var shouldRun = true;

        if (!profile.DesiredVpnEnabled)
        {
            reason = PolicyReason.UserOff;
            shouldRun = false;
        }
        else if (!network.HasUsableNetwork)
        {
            reason = PolicyReason.NoNetwork;
            shouldRun = false;
        }
        else if (exclusion is not null && !overrideActive)
        {
            reason = PolicyReason.ExcludedNetwork;
            shouldRun = false;
        }
        else if (overrideActive)
        {
            reason = PolicyReason.ManualOverride;
        }

        return new ProfilePlan(profile, exclusion, overrideActive, shouldRun, reason);
    }

    private async Task<ProfileRuntimeState> ApplyPlanAsync(ProfilePlan plan)
    {
        string? error = null;
        ServiceSnapshot? home = null;
        ServiceSnapshot? full = null;

        try
        {
            home = _serviceManager.Query(plan.Profile.HomeServiceName);
            full = _serviceManager.Query(plan.Profile.FullServiceName);

            if (home.State == WindowsServiceState.NotFound || full.State == WindowsServiceState.NotFound)
            {
                error = $"Die installierten Tunnel-Dienste für „{plan.Profile.DisplayName}“ wurden nicht gefunden. Bitte die Konfiguration erneut importieren.";
            }
            else if (plan.ShouldRun)
            {
                var selectedService = plan.Profile.RoutingMode == RoutingMode.HomeOnly ? home : full;
                var other = plan.Profile.RoutingMode == RoutingMode.HomeOnly ? full : home;

                if (IsPotentiallyActive(other))
                    await _serviceManager.StopAsync(other.Name);
                if (!selectedService.IsRunning)
                    await _serviceManager.StartAsync(selectedService.Name);
            }
            else
            {
                if (IsPotentiallyActive(home))
                    await _serviceManager.StopAsync(home.Name);
                if (IsPotentiallyActive(full))
                    await _serviceManager.StopAsync(full.Name);
            }

            home = _serviceManager.Query(plan.Profile.HomeServiceName);
            full = _serviceManager.Query(plan.Profile.FullServiceName);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { home = _serviceManager.Query(plan.Profile.HomeServiceName); } catch { }
            try { full = _serviceManager.Query(plan.Profile.FullServiceName); } catch { }
        }

        var active = plan.Profile.RoutingMode == RoutingMode.HomeOnly ? home : full;
        return new ProfileRuntimeState
        {
            Profile = plan.Profile,
            Exclusion = plan.Exclusion,
            Reason = plan.Reason,
            DesiredEnabled = plan.Profile.DesiredVpnEnabled,
            EffectiveEnabled = active?.IsRunning == true,
            ManualOverrideActive = plan.OverrideActive,
            CanManualOverride = plan.Exclusion?.Rule.AllowManualOverride == true,
            HomeService = home,
            FullService = full,
            Error = error
        };
    }

    private VpnProfile ResolveProfile(Guid? profileId)
    {
        _settings.NormalizeProfileSelection();
        var id = profileId ?? _settings.SelectedProfileId;
        var profile = id is Guid resolvedId ? _settings.Profiles.FirstOrDefault(x => x.Id == resolvedId) : null;
        return profile ?? throw new InvalidOperationException("No VPN profile is selected.");
    }

    private static bool IsPotentiallyActive(ServiceSnapshot service) =>
        service.IsRunning || service.State is WindowsServiceState.StartPending or WindowsServiceState.Paused;

    private void Publish(RuntimeState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(this, state);
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _ = RefreshAsync();
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => _ = RefreshAsync();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _timer?.Dispose();
        _gate.Dispose();
    }

    private sealed class ProfilePlan(
        VpnProfile profile,
        ExclusionMatch? exclusion,
        bool overrideActive,
        bool shouldRun,
        PolicyReason reason)
    {
        public VpnProfile Profile { get; } = profile;
        public ExclusionMatch? Exclusion { get; } = exclusion;
        public bool OverrideActive { get; } = overrideActive;
        public bool ShouldRun { get; set; } = shouldRun;
        public PolicyReason Reason { get; set; } = reason;
    }
}
