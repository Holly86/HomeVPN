using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HomeVpn.Core;
namespace HomeVpn.Infrastructure;

/// <summary>Strict single-peer wg-quick grammar; never accepts executable hooks.</summary>
public sealed class WireGuardConfig
{
    private readonly Dictionary<string, string> _interface, _peer;
    public string SourcePath { get; }
    public IReadOnlyList<string> AllowedIps { get; }
    public bool HasIpv6 => Values(_interface["Address"]).Any(x => x.Contains(':'));
    private WireGuardConfig(string source, Dictionary<string,string> iface, Dictionary<string,string> peer)
    { SourcePath = source; _interface = iface; _peer = peer; AllowedIps = Values(peer["AllowedIPs"]); }
    public static WireGuardConfig Parse(string path)
    {
        if (new FileInfo(path).Length > 65536) throw Invalid();
        return ParseText(File.ReadAllText(path), path);
    }
    public static WireGuardConfig ParseText(string text, string source = "")
    {
        if (text.Length > 65536 || text.Contains('\0')) throw Invalid();
        Dictionary<string,string>? section = null;
        var iface = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var peer = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        int interfaces = 0, peers = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.Equals("[Interface]", StringComparison.OrdinalIgnoreCase)) { section = iface; interfaces++; continue; }
            if (line.Equals("[Peer]", StringComparison.OrdinalIgnoreCase)) { section = peer; peers++; continue; }
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (section is null || parts.Length != 2 || parts[1].Length == 0) throw Invalid();
            var allowed = section == iface ? new[] { "PrivateKey", "Address", "DNS", "MTU", "ListenPort" }
                : new[] { "PublicKey", "PresharedKey", "Endpoint", "AllowedIPs", "PersistentKeepalive" };
            var key = allowed.FirstOrDefault(x => x.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
            if (key is null) throw new InvalidDataException("Nicht unterstützte Option. Skripte und zusätzliche Direktiven sind nicht erlaubt.");
            if (section.TryGetValue(key, out var previous))
            {
                if (key is not ("Address" or "DNS" or "AllowedIPs")) throw Invalid();
                section[key] = previous + ", " + parts[1];
            }
            else section.Add(key, parts[1]);
        }
        if (interfaces != 1 || peers != 1 || !iface.ContainsKey("Address") || !peer.ContainsKey("AllowedIPs") || !peer.ContainsKey("Endpoint")) throw Invalid();
        ValidateKey(iface, "PrivateKey", true); ValidateKey(peer, "PublicKey", true); ValidateKey(peer, "PresharedKey", false);
        if (Values(iface["Address"]).Length == 0 || Values(peer["AllowedIPs"]).Length == 0) throw Invalid();
        foreach (var cidr in Values(iface["Address"]).Concat(Values(peer["AllowedIPs"]))) if (!Cidr.TryParse(cidr, out _)) throw Invalid();
        foreach (var pair in new[] { (iface, "ListenPort", 0, 65535), (iface, "MTU", 576, 9000), (peer, "PersistentKeepalive", 0, 65535) })
            if (pair.Item1.TryGetValue(pair.Item2, out var n) && (!int.TryParse(n, out var value) || value < pair.Item3 || value > pair.Item4)) throw Invalid();
        var endpoint = peer["Endpoint"]; var colon = endpoint.LastIndexOf(':');
        if (colon <= 0 || !ushort.TryParse(endpoint[(colon + 1)..], out var port) || port == 0 || Uri.CheckHostName(endpoint[..colon].Trim('[', ']')) == UriHostNameType.Unknown) throw Invalid();
        if (iface.TryGetValue("DNS", out var dns) && Values(dns).Any(x => Uri.CheckHostName(x) == UriHostNameType.Unknown)) throw Invalid();
        return new WireGuardConfig(source, iface, peer);
    }
    private static void ValidateKey(Dictionary<string,string> section, string key, bool required)
    {
        if (!section.TryGetValue(key, out var value)) { if (required) throw Invalid(); return; }
        Span<byte> bytes = stackalloc byte[32];
        if (!Convert.TryFromBase64String(value, bytes, out int written) || written != 32 || bytes.IndexOfAnyExcept((byte)0) < 0) throw Invalid();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
    }
    private static InvalidDataException Invalid() => new("Ungültige WireGuard-Konfiguration. Interface, Schlüssel, Peer, Endpoint und IP-Netze prüfen.");
    private static string[] Values(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    public IReadOnlyList<string> DetectHomeCidrs() => AllowedIps.Where(x => Cidr.TryParse(x, out var c) && c!.PrefixLength > 1).Distinct().ToArray();
    public string CreateHomeOnlyVariant(IEnumerable<string> homeCidrs)
    {
        var routes = homeCidrs.Select(x => Cidr.TryParse(x, out var c) && c!.PrefixLength > 1 ? c.ToString() : throw new InvalidDataException("Nur Heimnetz benötigt konkrete Zielnetze; /0 und /1 sind nicht erlaubt.")).Distinct().Order().ToArray();
        if (routes.Length == 0) throw new InvalidDataException("Mindestens ein Zielnetz ist erforderlich.");
        return Render(routes, true);
    }
    public string CreateFullTunnelVariant() => Render(HasIpv6 ? ["0.0.0.0/0", "::/0"] : ["0.0.0.0/0"], false);
    public string CanonicalText() => Render(AllowedIps, false);
    private string Render(IEnumerable<string> routes, bool split)
    {
        var b = new StringBuilder("[Interface]\n");
        foreach (var key in new[] { "PrivateKey", "Address", "DNS", "MTU", "ListenPort" })
            // Split retains local DNS to avoid replacing it with an unreachable remote DNS server.
            if (!(split && key == "DNS") && _interface.TryGetValue(key, out var value)) b.Append(key).Append(" = ").Append(value).Append('\n');
        b.Append("\n[Peer]\n");
        foreach (var key in new[] { "PublicKey", "PresharedKey", "Endpoint", "AllowedIPs", "PersistentKeepalive" })
            if (key == "AllowedIPs") b.Append("AllowedIPs = ").AppendJoin(", ", routes).Append('\n');
            else if (_peer.TryGetValue(key, out var value)) b.Append(key).Append(" = ").Append(value).Append('\n');
        return b.ToString();
    }
    public static string SanitizeTunnelName(string value) => Regex.Replace(value, "[^A-Za-z0-9.-]", "-").Trim('.','-') is { Length: > 0 } s ? s[..Math.Min(s.Length, 32)] : "VPN";
}
