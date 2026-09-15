using HomeVpn.Core;
using HomeVpn.Models;
namespace HomeVpn.Services;
public sealed record ProfilePlan(VpnProfile Profile, ExclusionMatch? Exclusion, bool OverrideActive, bool ShouldRun, PolicyReason Reason);
public static class PolicyPlanner
{
    public static IReadOnlyList<ProfilePlan> Evaluate(AppSettings settings, NetworkSnapshot network, IReadOnlyDictionary<Guid,string> overrides)
    {
        var plans = settings.Profiles.Select(profile =>
        {
            var exclusion = NetworkRuleMatcher.FindMatch(network, settings.ExcludedNetworks, profile.Id);
            bool manual = exclusion?.Rule.AllowManualOverride == true && overrides.TryGetValue(profile.Id, out var fingerprint) && fingerprint == network.Fingerprint;
            var reason = !profile.DesiredVpnEnabled ? PolicyReason.UserOff : !network.HasUsableNetwork ? PolicyReason.NoNetwork : exclusion is not null && !manual ? PolicyReason.ExcludedNetwork : manual ? PolicyReason.ManualOverride : PolicyReason.Normal;
            bool run = reason is PolicyReason.Normal or PolicyReason.ManualOverride;
            if (profile.Backend != TunnelBackendKind.EmbeddedWireGuard) { run = false; reason = PolicyReason.MigrationRequired; }
            return new ProfilePlan(profile, exclusion, manual, run, reason);
        }).ToList();
        var ordered = plans.Where(x => x.ShouldRun).OrderByDescending(x => x.Profile.Id == settings.SelectedProfileId).ThenByDescending(x => x.Profile.Id == settings.PrimaryProfileId).ToArray();
        var full = ordered.FirstOrDefault(x => x.Profile.RoutingMode == RoutingMode.FullTunnel);
        var accepted = new List<ProfilePlan>();
        foreach (var plan in ordered)
        {
            bool conflict = full is not null ? plan != full : accepted.Any(x => Overlap(x.Profile, plan.Profile)
                || HomeVpn.Infrastructure.SplitDns.Conflicts(x.Profile.SplitDns, plan.Profile.SplitDns));
            if (!ValidSplit(plan.Profile)) conflict = true;
            if (conflict) plans[plans.IndexOf(plan)] = plan with { ShouldRun = false, Reason = PolicyReason.RouteConflict };
            else accepted.Add(plan);
        }
        return plans;
    }
    private static bool ValidSplit(VpnProfile p) => p.RoutingMode == RoutingMode.FullTunnel || (p.HomeCidrs.Count > 0 && p.HomeCidrs.All(x => Cidr.TryParse(x, out var c) && c!.PrefixLength > 1));
    private static bool Overlap(VpnProfile a, VpnProfile b) => a.HomeCidrs.Any(x => b.HomeCidrs.Any(y => !Cidr.TryParse(x, out var left) || !Cidr.TryParse(y, out var right) || left!.Overlaps(right!)));
}
