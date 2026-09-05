using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using HomeVpn.Core;
using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public sealed class NetworkDetector
{
    private static readonly string[] VirtualHints =
    [
        "wireguard", "tailscale", "zerotier", "hyper-v", "vethernet", "virtualbox",
        "vmware", "docker", "wsl", "loopback", "npcap"
    ];

    public NetworkSnapshot GetSnapshot()
    {
        IReadOnlyList<WifiConnection> wifiConnections;
        try
        {
            wifiConnections = WlanApi.GetConnectedNetworks();
        }
        catch
        {
            wifiConnections = [];
        }

        var wifiById = wifiConnections.ToDictionary(x => NormalizeGuid(x.InterfaceGuid.ToString()), StringComparer.OrdinalIgnoreCase);
        var interfaces = new List<NetworkInterfaceSnapshot>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WifiConnection? primaryWifi = null;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                IsLikelyVirtual(nic))
                continue;

            var addresses = new List<IPAddress>();
            var networks = new List<string>();

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (IPAddress.IsLoopback(unicast.Address) ||
                    (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6 && unicast.Address.IsIPv6LinkLocal))
                    continue;

                addresses.Add(unicast.Address);
                try
                {
                    networks.Add(Cidr.FromAddressAndPrefix(unicast.Address, unicast.PrefixLength));
                }
                catch
                {
                    // Ignore malformed or unsupported interface address metadata.
                }
            }

            if (addresses.Count == 0)
                continue;

            var snapshot = new NetworkInterfaceSnapshot
            {
                Id = nic.Id,
                Name = nic.Name,
                Description = nic.Description,
                Addresses = addresses,
                NetworkCidrs = networks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            interfaces.Add(snapshot);
            names.Add(nic.Name);
            names.Add(nic.Description);

            if (wifiById.TryGetValue(NormalizeGuid(nic.Id), out var wifi))
            {
                primaryWifi ??= wifi;
                names.Add(wifi.Ssid);
            }
        }

        if (primaryWifi is null && wifiConnections.Count > 0)
        {
            primaryWifi = wifiConnections[0];
            names.Add(primaryWifi.Ssid);
        }

        var hasNetwork = interfaces.Count > 0;
        var displayName = primaryWifi?.Ssid
            ?? interfaces.FirstOrDefault()?.Name
            ?? "Kein Netzwerk";

        var fingerprintInput = string.Join("|",
            new[] { primaryWifi?.Ssid ?? "" }
                .Concat(interfaces.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(x => new[] { x.Id }.Concat(x.NetworkCidrs.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)))));

        var fingerprint = hasNetwork ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))) : "offline";

        return new NetworkSnapshot
        {
            Fingerprint = fingerprint,
            DisplayName = displayName,
            WifiSsid = primaryWifi?.Ssid,
            IsWifi = primaryWifi is not null,
            IsOpenWifi = primaryWifi is { SecurityEnabled: false },
            HasUsableNetwork = hasNetwork,
            Interfaces = interfaces,
            NameCandidates = names.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
        };
    }

    public IReadOnlyList<string> GetPrivateIpv4Networks(NetworkSnapshot snapshot) => snapshot.Interfaces
        .SelectMany(i => i.NetworkCidrs)
        .Select(c => Cidr.TryParse(c, out var parsed) ? parsed : null)
        .Where(c => c is not null && c.Network.AddressFamily == AddressFamily.InterNetwork && PrivateNetwork.IsPrivate(c.Network))
        .Select(c => c!.ToString())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool IsLikelyVirtual(NetworkInterface nic)
    {
        var haystack = $"{nic.Name} {nic.Description}".ToLowerInvariant();
        return VirtualHints.Any(haystack.Contains);
    }

    private static string NormalizeGuid(string value)
    {
        if (Guid.TryParse(value.Trim('{', '}'), out var guid))
            return guid.ToString("D");
        return value.Trim().Trim('{', '}');
    }
}
