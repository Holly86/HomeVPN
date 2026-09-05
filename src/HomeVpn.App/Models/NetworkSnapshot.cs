using System.Net;

namespace HomeVpn.Models;

public sealed class NetworkSnapshot
{
    public static NetworkSnapshot Empty { get; } = new();

    public string Fingerprint { get; init; } = "offline";
    public string DisplayName { get; init; } = "Kein Netzwerk";
    public string? WifiSsid { get; init; }
    public bool IsWifi { get; init; }
    public bool IsOpenWifi { get; init; }
    public bool HasUsableNetwork { get; init; }
    public IReadOnlyList<NetworkInterfaceSnapshot> Interfaces { get; init; } = [];
    public IReadOnlyList<string> NameCandidates { get; init; } = [];

    public IEnumerable<IPAddress> LocalAddresses => Interfaces.SelectMany(x => x.Addresses);
    public IEnumerable<string> LocalNetworks => Interfaces.SelectMany(x => x.NetworkCidrs);
}

public sealed class NetworkInterfaceSnapshot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];
    public IReadOnlyList<string> NetworkCidrs { get; init; } = [];
}

public sealed class ExclusionMatch
{
    public required ExcludedNetworkRule Rule { get; init; }
}
