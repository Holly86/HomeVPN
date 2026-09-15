using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public static class TunnelIdentity
{
    // Upstream requires <=32 filename characters and WireGuardTunnel$ SCM prefix.
    // Base32 encodes ALL 128 GUID bits (no truncated hash, no editable display name).
    public static string Name(Guid id, RoutingMode mode)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var encoded = new System.Text.StringBuilder("HVPN");
        int value = 0, bits = 0;
        foreach (byte b in id.ToByteArray())
        {
            value = (value << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; encoded.Append(alphabet[(value >> bits) & 31]); }
        }
        if (bits > 0) encoded.Append(alphabet[(value << (5 - bits)) & 31]);
        return encoded.Append(mode == RoutingMode.HomeOnly ? 'S' : 'F').ToString();
    }
    public static string Service(Guid id, RoutingMode mode) => "WireGuardTunnel$" + Name(id, mode);
    public static string ServiceAcl(string sid)
    {
        _ = new System.Security.Principal.SecurityIdentifier(sid);
        return $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;LCRPWPLO;;;{sid})";
    }
}

public interface ITunnelController
{
    HomeVpn.Models.ServiceSnapshot Query(string name);
    Task StartAsync(string name, CancellationToken cancellationToken = default);
    Task StopAsync(string name, CancellationToken cancellationToken = default);
}

public interface ITunnelProvisioner
{
    Task<VpnProfile> ProvisionAsync(string configuration, string displayName, IReadOnlyList<string> routes, string userSid);
    Task RemoveAsync(Guid id, bool deleteSecrets);
}
