using System.Globalization;
using System.Net;
using System.Net.Sockets;
using HomeVpn.Core;
using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public static class SplitDns
{
    public static SplitDnsSettings Normalize(SplitDnsSettings? settings, IReadOnlyList<string> routes)
    {
        settings ??= new();
        if (!settings.Enabled)
        {
            if (settings.Domains.Count != 0) throw new InvalidDataException("Für Heimnetz-Domänen bitte einen DNS-Server angeben.");
            return new();
        }
        if (!IPAddress.TryParse(settings.Server.Trim(), out var address) || IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast
            || address.IsIPv6LinkLocal || address.IsIPv4MappedToIPv6
            || (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            || (address.AddressFamily == AddressFamily.InterNetwork && (address.GetAddressBytes()[0] >= 224 || address.GetAddressBytes()[0] == 0)))
            throw new InvalidDataException("Bitte eine gültige, über den Tunnel erreichbare DNS-Server-IP eingeben.");
        if (!routes.Any(x => Cidr.TryParse(x, out var c) && c!.PrefixLength > 1 && c.Contains(address)))
            throw new InvalidDataException("Der Heim-DNS muss innerhalb eines konfigurierten Split-Zielnetzes liegen.");
        if (settings.Domains.Count is < 1 or > 16) throw new InvalidDataException("Bitte 1 bis 16 Heimnetz-Domänen angeben, zum Beispiel fritz.box oder home.arpa.");
        var domains = settings.Domains.Select(NormalizeDomain).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return new() { Server = address.ToString(), Domains = domains };
    }

    public static string NormalizeDomain(string value)
    {
        var domain = value.Trim().TrimStart('.').TrimEnd('.');
        try { domain = new IdnMapping().GetAscii(domain).ToLowerInvariant(); }
        catch (ArgumentException) { throw new InvalidDataException("Ungültige Heimnetz-Domäne."); }
        if (domain.Length is < 1 or > 253 || IPAddress.TryParse(domain, out _) || domain.Split('.').Any(label =>
            label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' || label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))))
            throw new InvalidDataException("Heimnetz-Domänen ohne URL, Wildcard, Port oder Pfad angeben.");
        return domain;
    }

    public static bool Overlap(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase) || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);
    public static bool Conflicts(SplitDnsSettings a, SplitDnsSettings b) => a.Enabled && b.Enabled
        && a.Domains.Any(x => b.Domains.Any(y => Overlap(x, y)));
    public static string[] Namespaces(SplitDnsSettings settings) => settings.Domains.SelectMany(x => new[] { x, "." + x }).ToArray();
    public static bool ShouldApply(bool splitRunning, bool adapterUp, bool fullRunning) => splitRunning && adapterUp && !fullRunning;
}
