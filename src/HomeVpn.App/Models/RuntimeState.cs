namespace HomeVpn.Models;

public enum PolicyReason
{
    NoProfile,
    UserOff,
    NoNetwork,
    ExcludedNetwork,
    ManualOverride,
    RouteConflict,
    Normal
}

public enum RecommendationSeverity
{
    None,
    Info,
    Warning
}

public sealed class ProfileRuntimeState
{
    public required VpnProfile Profile { get; init; }
    public ExclusionMatch? Exclusion { get; init; }
    public PolicyReason Reason { get; init; }
    public bool DesiredEnabled { get; init; }
    public bool EffectiveEnabled { get; init; }
    public bool ManualOverrideActive { get; init; }
    public bool CanManualOverride { get; init; }
    public ServiceSnapshot? HomeService { get; init; }
    public ServiceSnapshot? FullService { get; init; }
    public string? Error { get; init; }

    public RoutingMode RoutingMode => Profile.RoutingMode;
    public ServiceSnapshot? ActiveService => RoutingMode == RoutingMode.HomeOnly ? HomeService : FullService;
}

public sealed class RuntimeState
{
    public NetworkSnapshot Network { get; init; } = NetworkSnapshot.Empty;
    public Guid? SelectedProfileId { get; init; }
    public Guid? PrimaryProfileId { get; init; }
    public IReadOnlyList<ProfileRuntimeState> Profiles { get; init; } = [];
    public RecommendationSeverity Recommendation { get; init; }

    public ProfileRuntimeState? SelectedProfile => SelectedProfileId is Guid id
        ? Profiles.FirstOrDefault(x => x.Profile.Id == id)
        : null;

    // Convenience properties keep the UI/tray focused on the currently selected profile.
    public ExclusionMatch? Exclusion => SelectedProfile?.Exclusion;
    public PolicyReason Reason => SelectedProfile?.Reason ?? PolicyReason.NoProfile;
    public bool DesiredEnabled => SelectedProfile?.DesiredEnabled == true;
    public bool EffectiveEnabled => SelectedProfile?.EffectiveEnabled == true;
    public bool ManualOverrideActive => SelectedProfile?.ManualOverrideActive == true;
    public bool CanManualOverride => SelectedProfile?.CanManualOverride == true;
    public RoutingMode RoutingMode => SelectedProfile?.RoutingMode ?? RoutingMode.HomeOnly;
    public ServiceSnapshot? HomeService => SelectedProfile?.HomeService;
    public ServiceSnapshot? FullService => SelectedProfile?.FullService;
    public ServiceSnapshot? ActiveService => SelectedProfile?.ActiveService;
    public string? Error => SelectedProfile?.Error;
}

public enum WindowsServiceState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7,
    NotFound = 100
}

public sealed class ServiceSnapshot
{
    public string Name { get; init; } = "";
    public WindowsServiceState State { get; init; }
    public uint ProcessId { get; init; }
    public DateTimeOffset? ProcessStartedAt { get; init; }

    public bool IsRunning => State == WindowsServiceState.Running;
}
