using System.Text.RegularExpressions;
using HomeVpn.Models;

namespace HomeVpn.Core;

public static class NetworkRuleMatcher
{
    public static ExclusionMatch? FindMatch(NetworkSnapshot snapshot, IEnumerable<ExcludedNetworkRule> rules, Guid? profileId = null)
    {
        foreach (var rule in rules)
        {
            if (profileId is Guid id && !rule.AppliesTo(id))
                continue;

            if (Matches(snapshot, rule))
                return new ExclusionMatch { Rule = rule };
        }

        return null;
    }

    public static bool Matches(NetworkSnapshot snapshot, ExcludedNetworkRule rule)
    {
        var hasName = !string.IsNullOrWhiteSpace(rule.NetworkNamePattern);
        var hasSubnet = !string.IsNullOrWhiteSpace(rule.SubnetCidr);
        if (!hasName && !hasSubnet)
            return false;

        if (hasName && !snapshot.NameCandidates.Any(x => WildcardMatch(x, rule.NetworkNamePattern!)))
            return false;

        if (hasSubnet)
        {
            if (!Cidr.TryParse(rule.SubnetCidr, out var subnet) || subnet is null)
                return false;

            if (!snapshot.LocalAddresses.Any(subnet.Contains))
                return false;
        }

        return true;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
