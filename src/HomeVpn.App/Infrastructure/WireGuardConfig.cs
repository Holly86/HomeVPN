using System.Text.RegularExpressions;
using HomeVpn.Core;

namespace HomeVpn.Infrastructure;

public sealed class WireGuardConfig
{
    private static readonly HashSet<string> FullRouteTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "0.0.0.0/0", "::/0", "0.0.0.0/1", "128.0.0.0/1", "::/1", "8000::/1"
    };

    private readonly string[] _lines;
    private readonly int _peerStart;
    private readonly int _peerEnd;

    public string SourcePath { get; }
    public IReadOnlyList<string> AllowedIps { get; }
    public bool HasIpv6 { get; }

    private WireGuardConfig(
        string sourcePath,
        string[] lines,
        int peerStart,
        int peerEnd,
        IReadOnlyList<string> allowedIps,
        bool hasIpv6)
    {
        SourcePath = sourcePath;
        _lines = lines;
        _peerStart = peerStart;
        _peerEnd = peerEnd;
        AllowedIps = allowedIps;
        HasIpv6 = hasIpv6;
    }

    public static WireGuardConfig Parse(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("WireGuard configuration not found.", path);

        var lines = File.ReadAllLines(path);
        var section = string.Empty;
        var interfaceCount = 0;
        var peerCount = 0;
        var peerStart = -1;
        var peerEnd = lines.Length;
        var allowed = new List<string>();
        var hasIpv6 = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (section.Equals("Peer", StringComparison.OrdinalIgnoreCase) && peerEnd == lines.Length)
                    peerEnd = i;

                section = trimmed.Trim('[', ']').Trim();
                if (section.Equals("Interface", StringComparison.OrdinalIgnoreCase))
                    interfaceCount++;
                if (section.Equals("Peer", StringComparison.OrdinalIgnoreCase))
                {
                    peerCount++;
                    if (peerStart < 0)
                        peerStart = i;
                }
                continue;
            }

            if (TryReadValue(trimmed, "Address", out var addressValue) && addressValue.Contains(':'))
                hasIpv6 = true;

            if (section.Equals("Peer", StringComparison.OrdinalIgnoreCase) &&
                TryReadValue(trimmed, "AllowedIPs", out var allowedValue))
            {
                var values = allowedValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                allowed.AddRange(values);
                if (values.Any(x => x.Contains(':')))
                    hasIpv6 = true;
            }
        }

        if (interfaceCount != 1)
            throw new InvalidDataException("The imported file must contain exactly one [Interface] section.");
        if (peerCount != 1 || peerStart < 0)
            throw new InvalidDataException("The imported file must contain exactly one [Peer] section.");
        if (allowed.Count == 0)
            throw new InvalidDataException("The [Peer] section does not contain AllowedIPs.");

        return new WireGuardConfig(path, lines, peerStart, peerEnd, allowed, hasIpv6);
    }

    public IReadOnlyList<string> DetectHomeCidrs()
    {
        return AllowedIps
            .Where(x => !FullRouteTokens.Contains(x))
            .Where(x => Cidr.TryParse(x, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string CreateHomeOnlyVariant(IEnumerable<string> homeCidrs)
    {
        var cidrs = homeCidrs
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cidrs.Length == 0)
            throw new InvalidDataException("At least one home-network CIDR is required for Home-only routing.");
        foreach (var cidr in cidrs)
        {
            if (!Cidr.TryParse(cidr, out _))
                throw new InvalidDataException($"Invalid CIDR: {cidr}");
        }

        return RewriteAllowedIps(cidrs);
    }

    public string CreateFullTunnelVariant()
    {
        var routes = HasIpv6
            ? new[] { "0.0.0.0/0", "::/0" }
            : new[] { "0.0.0.0/0" };
        return RewriteAllowedIps(routes);
    }

    public static string SanitizeTunnelName(string value)
    {
        // WireGuard for Windows accepts tunnel names matching
        // ^[a-zA-Z0-9_=+.-]{1,32}$ and rejects Windows reserved device names.
        // We deliberately keep a conservative, filename-safe subset and normalize
        // user/file-derived names rather than surfacing avoidable setup errors.
        var cleaned = Regex.Replace((value ?? string.Empty).Trim(), @"[^A-Za-z0-9_=+.-]+", "-")
            .Trim(' ', '.', '-');

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Home";

        if (cleaned.Length > 32)
            cleaned = cleaned[..32].TrimEnd('.');

        if (Regex.IsMatch(cleaned, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            cleaned = "VPN-" + cleaned;

        if (cleaned.Length > 32)
            cleaned = cleaned[..32].TrimEnd('.');

        return string.IsNullOrWhiteSpace(cleaned) ? "Home" : cleaned;
    }

    private string RewriteAllowedIps(IReadOnlyList<string> newAllowedIps)
    {
        var output = _lines.ToList();
        var replaced = false;

        for (var i = _peerStart + 1; i < _peerEnd; i++)
        {
            var trimmed = output[i].Trim();
            if (!TryReadValue(trimmed, "AllowedIPs", out _))
                continue;

            var indentLength = output[i].Length - output[i].TrimStart().Length;
            var indent = indentLength > 0 ? output[i][..indentLength] : string.Empty;
            output[i] = $"{indent}AllowedIPs = {string.Join(", ", newAllowedIps)}";
            replaced = true;
            break;
        }

        if (!replaced)
            output.Insert(_peerStart + 1, $"AllowedIPs = {string.Join(", ", newAllowedIps)}");

        return string.Join(Environment.NewLine, output) + Environment.NewLine;
    }

    private static bool TryReadValue(string line, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
            return false;

        var equalsIndex = line.IndexOf('=');
        if (equalsIndex <= 0)
            return false;

        var left = line[..equalsIndex].Trim();
        if (!left.Equals(key, StringComparison.OrdinalIgnoreCase))
            return false;

        value = line[(equalsIndex + 1)..].Trim();
        return true;
    }
}
