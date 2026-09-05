namespace HomeVpn.Models;

public enum RoutingMode
{
    HomeOnly,
    FullTunnel
}

public enum TunnelBackendKind
{
    OfficialWireGuard,
    EmbeddedWireGuard
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public string? SettingsGeneration { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public Guid? PrimaryProfileId { get; set; }
    public Guid? SelectedProfileId { get; set; }
    public List<VpnProfile> Profiles { get; set; } = [];
    public List<ExcludedNetworkRule> ExcludedNetworks { get; set; } = [];

    public VpnProfile? GetPrimaryProfile()
    {
        NormalizeProfileSelection();
        return PrimaryProfileId is Guid id ? Profiles.FirstOrDefault(x => x.Id == id) : null;
    }

    public VpnProfile? GetSelectedProfile()
    {
        NormalizeProfileSelection();
        return SelectedProfileId is Guid id ? Profiles.FirstOrDefault(x => x.Id == id) : null;
    }

    public void NormalizeProfileSelection()
    {
        if (Profiles.Count == 0)
        {
            PrimaryProfileId = null;
            SelectedProfileId = null;
            return;
        }

        if (PrimaryProfileId is null || Profiles.All(x => x.Id != PrimaryProfileId.Value))
            PrimaryProfileId = Profiles[0].Id;

        if (SelectedProfileId is null || Profiles.All(x => x.Id != SelectedProfileId.Value))
            SelectedProfileId = PrimaryProfileId;
    }
}

public sealed class VpnProfile : System.ComponentModel.INotifyPropertyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    private string _displayName = "VPN";
    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName == value) return; _displayName = value; PropertyChanged?.Invoke(this, new(nameof(DisplayName))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string HomeTunnelName { get; set; } = "Home";
    public string FullTunnelName { get; set; } = "Home-Full";
    public List<string> HomeCidrs { get; set; } = [];
    public SplitDnsSettings SplitDns { get; set; } = new();
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool DesiredVpnEnabled { get; set; }
    public RoutingMode RoutingMode { get; set; } = RoutingMode.HomeOnly;
    public TunnelBackendKind Backend { get; set; } = TunnelBackendKind.OfficialWireGuard;

    public string HomeServiceName => Backend == TunnelBackendKind.EmbeddedWireGuard ? HomeVpn.Infrastructure.TunnelIdentity.Service(Id, RoutingMode.HomeOnly) : $"WireGuardTunnel${HomeTunnelName}";
    public string FullServiceName => Backend == TunnelBackendKind.EmbeddedWireGuard ? HomeVpn.Infrastructure.TunnelIdentity.Service(Id, RoutingMode.FullTunnel) : $"WireGuardTunnel${FullTunnelName}";
}

public sealed class SplitDnsSettings
{
    public string Server { get; set; } = "";
    public List<string> Domains { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Enabled => !string.IsNullOrWhiteSpace(Server);
}

public sealed class ExcludedNetworkRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Ausgeschlossenes Netzwerk";
    public string? NetworkNamePattern { get; set; }
    public string? SubnetCidr { get; set; }
    public bool AllowManualOverride { get; set; } = true;

    // Empty means global. Profile-scoped rules are useful for home-network exclusions:
    // being at your own home should suppress your own home VPN, while a second tunnel
    // to a family member can still be used.
    public List<Guid> ProfileIds { get; set; } = [];

    public bool AppliesTo(Guid profileId) => ProfileIds.Count == 0 || ProfileIds.Contains(profileId);

    public ExcludedNetworkRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        NetworkNamePattern = NetworkNamePattern,
        SubnetCidr = SubnetCidr,
        AllowManualOverride = AllowManualOverride,
        ProfileIds = ProfileIds.ToList()
    };
}
