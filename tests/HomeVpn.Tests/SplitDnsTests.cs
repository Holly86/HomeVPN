using System.Text.Json;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
using HomeVpn.Services;
using Xunit;

namespace HomeVpn.Tests;

public sealed class SplitDnsTests
{
    [Fact] public void OldProfilesDefaultToDisabled()
    {
        var profile = JsonSerializer.Deserialize<VpnProfile>("{}")!;
        Assert.False(profile.SplitDns.Enabled);
        Assert.False(SplitDns.Normalize(null, []).Enabled);
    }
    [Fact] public void NormalizesAndScopesApexAndChildren()
    {
        var dns = SplitDns.Normalize(new() { Server=" 10.77.0.53 ", Domains=[".HOME.ARPA.","home.arpa","fritz.box"] }, ["10.77.0.0/24"]);
        Assert.Equal("10.77.0.53",dns.Server);
        Assert.Equal(["fritz.box","home.arpa"],dns.Domains);
        Assert.Equal(["fritz.box",".fritz.box","home.arpa",".home.arpa"],SplitDns.Namespaces(dns));
        Assert.DoesNotContain(".",SplitDns.Namespaces(dns));
    }
    [Theory][InlineData(".")][InlineData("*.home.arpa")][InlineData("https://home.arpa")][InlineData("home..arpa")][InlineData("home.arpa;evil")][InlineData("-bad.arpa")][InlineData("10.77.0.53")]
    public void InvalidDomains(string domain) => Assert.Throws<InvalidDataException>(() => SplitDns.NormalizeDomain(domain));
    [Fact] public void IdnAndSingleLabelZones() { Assert.Equal("xn--bro-hoa.home.arpa",SplitDns.NormalizeDomain("büro.home.arpa")); Assert.Equal("lan",SplitDns.NormalizeDomain("lan")); }
    [Theory][InlineData("127.0.0.1")][InlineData("0.0.0.0")][InlineData("224.0.0.1")][InlineData("1.1.1.1")][InlineData("dns.home.arpa")][InlineData("10.77.0.53:53")][InlineData("fe80::1%1")]
    public void RejectsInvalidOrUnroutedServer(string server) => Assert.Throws<InvalidDataException>(() => SplitDns.Normalize(new() { Server=server,Domains=["home.arpa"] },["10.77.0.0/24"]));
    [Fact] public void Ipv6MustBeInIpv6TargetNetwork()
    {
        Assert.Throws<InvalidDataException>(() => SplitDns.Normalize(new() { Server="fd77::53",Domains=["home.arpa"] },["10.77.0.0/24"]));
        Assert.Equal("fd77::53",SplitDns.Normalize(new() { Server="fd77::53",Domains=["home.arpa"] },["fd77::/64"]).Server);
    }
    [Fact] public void RequiresDomainsAndServerTogether()
    {
        Assert.Throws<InvalidDataException>(() => SplitDns.Normalize(new() { Server="10.77.0.53" },["10.77.0.0/24"]));
        Assert.Throws<InvalidDataException>(() => SplitDns.Normalize(new() { Domains=["home.arpa"] },[]));
    }
    [Theory][InlineData(true,true,false,true)][InlineData(false,true,false,false)][InlineData(true,false,false,false)][InlineData(true,true,true,false)]
    public void OnlyConnectedSplitApplies(bool split,bool adapter,bool full,bool expected) => Assert.Equal(expected,SplitDns.ShouldApply(split,adapter,full));
    [Fact] public void ConflictBlocksOneProfileWithoutChangingDesiredState()
    {
        var a=RuntimeTests.Profile("10.77.0.0/24"); var b=RuntimeTests.Profile("10.88.0.0/24");
        a.SplitDns=new() { Server="10.77.0.53",Domains=["home.arpa"] };
        b.SplitDns=new() { Server="10.88.0.53",Domains=["lab.home.arpa"] };
        var plans=PolicyPlanner.Evaluate(new() { Profiles=[a,b],PrimaryProfileId=a.Id },RuntimeTests.Online(),new Dictionary<Guid,string>());
        Assert.Equal(a.Id,plans.Single(x=>x.ShouldRun).Profile.Id);
        Assert.Equal(PolicyReason.RouteConflict,plans.Single(x=>x.Profile.Id==b.Id).Reason);
        Assert.True(b.DesiredVpnEnabled);
        b.SplitDns.Domains=["other.home.arpa.example"];
        Assert.Equal(2,PolicyPlanner.Evaluate(new() { Profiles=[a,b] },RuntimeTests.Online(),new Dictionary<Guid,string>()).Count(x=>x.ShouldRun));
    }
    [Fact] public void FullModeDoesNotApplySplitDnsArbitration()
    {
        var p=RuntimeTests.Profile("10.77.0.0/24"); p.RoutingMode=RoutingMode.FullTunnel;
        p.SplitDns=new() { Server="10.77.0.53",Domains=["home.arpa"] };
        Assert.True(PolicyPlanner.Evaluate(new() { Profiles=[p] },RuntimeTests.Online(),new Dictionary<Guid,string>()).Single().ShouldRun);
    }
    [Fact] public void RoundTripContainsOnlyDnsMetadata()
    {
        var profile=RuntimeTests.Profile("10.77.0.0/24"); profile.SplitDns=new() { Server="10.77.0.53",Domains=["home.arpa"] };
        var json=JsonSerializer.Serialize(profile); var copy=JsonSerializer.Deserialize<VpnProfile>(json)!;
        Assert.Equal(profile.SplitDns.Server,copy.SplitDns.Server); Assert.Equal(profile.SplitDns.Domains,copy.SplitDns.Domains);
        Assert.DoesNotContain("PrivateKey",json); Assert.DoesNotContain("PresharedKey",json);
    }
}
