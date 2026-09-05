using System.Net;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Text.Json;
using HomeVpn.Core;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
using HomeVpn.Services;
using Xunit;
namespace HomeVpn.Tests;

public class RuntimeTests
{
    // Synthetic ephemeral keys, generated in memory. Never a real VPN configuration or persisted fixture.
    public static string Config(bool ipv6=false) => "[Interface]\nPrivateKey = " + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\nAddress = 10.77.0.2/32" + (ipv6 ? ", fd77::2/128" : "") + "\nDNS = 10.77.0.1\n[Peer]\nPublicKey = " + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\nEndpoint = example.com:51820\nAllowedIPs = 10.77.0.0/24\nPersistentKeepalive = 25\n";
    [Theory][InlineData("192.168.1.0/24","192.168.1.99",true)][InlineData("192.168.1.0/24","192.168.2.1",false)][InlineData("fd00::/8","fd01::1",true)][InlineData("fd00::/8","10.0.0.1",false)]
    public void Contains(string network,string address,bool expected) { Assert.True(Cidr.TryParse(network,out var c)); Assert.Equal(expected,c!.Contains(IPAddress.Parse(address))); }
    [Theory][InlineData("10.0.0.0/8","10.1.0.0/16",true)][InlineData("10.1.0.0/16","10.0.0.0/8",true)][InlineData("10.1.0.0/16","10.2.0.0/16",false)][InlineData("10.0.0.0/8","fd00::/8",false)][InlineData("fd00::/8","fd01::/16",true)][InlineData("::/0","2001:db8::/32",true)]
    public void Overlap(string a,string b,bool expected) { Cidr.TryParse(a,out var x); Cidr.TryParse(b,out var y); Assert.Equal(expected,x!.Overlaps(y!)); }
    [Theory][InlineData("10.0.0.1/33")][InlineData("fd00::/129")][InlineData("../x")][InlineData("10.1.1.1/-1")][InlineData("")]
    public void InvalidCidr(string value) => Assert.False(Cidr.TryParse(value,out _));
    [Theory][InlineData(false)][InlineData(true)]
    public void Transformations(bool ipv6)
    {
        var config=WireGuardConfig.ParseText(Config(ipv6));
        var split=config.CreateHomeOnlyVariant(["10.88.0.1/24"]);
        Assert.Equal(["10.88.0.0/24"],WireGuardConfig.ParseText(split).AllowedIps);
        Assert.DoesNotContain("DNS",split);
        var full=WireGuardConfig.ParseText(config.CreateFullTunnelVariant());
        Assert.Contains("0.0.0.0/0",full.AllowedIps); Assert.Equal(ipv6,full.AllowedIps.Contains("::/0"));
    }
    [Fact] public void RepeatedAllowedIpsCannotSurviveSplit() { var config=WireGuardConfig.ParseText(Config()+"AllowedIPs = 0.0.0.0/0\n"); Assert.DoesNotContain("0.0.0.0/0",config.CreateHomeOnlyVariant(["10.0.0.0/8"])); }
    [Theory][InlineData("PostUp = evil")][InlineData("PreDown = evil")][InlineData("SaveConfig = true")][InlineData("Table = off")][InlineData("[Peer]")][InlineData("PublicKey = invalid")]
    public void RejectUnsafeOrInvalidConfig(string suffix) => Assert.Throws<InvalidDataException>(()=>WireGuardConfig.ParseText(Config()+suffix));
    [Theory][InlineData("0.0.0.0/0")][InlineData("0.0.0.0/1")][InlineData("::/0")][InlineData("::/1")]
    public void SplitRejectsDefaultRoute(string route) => Assert.Throws<InvalidDataException>(()=>WireGuardConfig.ParseText(Config()).CreateHomeOnlyVariant([route]));
    [Fact] public void InvalidKeyNeverEchoed() { var secret="invalid-secret-value"; var e=Assert.Throws<InvalidDataException>(()=>WireGuardConfig.ParseText(Config().Replace("Address =", "PrivateKey = " + secret + "\nAddress ="))); Assert.DoesNotContain(secret,e.Message); }
    [Fact] public void StableIdentity() { var id=Guid.NewGuid(); var a=TunnelIdentity.Name(id,RoutingMode.HomeOnly); Assert.Equal(31,a.Length); Assert.Equal(a,TunnelIdentity.Name(id,RoutingMode.HomeOnly)); Assert.NotEqual(a,TunnelIdentity.Name(id,RoutingMode.FullTunnel)); Assert.NotEqual(a,TunnelIdentity.Name(Guid.NewGuid(),RoutingMode.HomeOnly)); }
    [Fact] public void RenameDoesNotChangeService() { var p=Profile("10.0.0.0/8"); var name=p.HomeServiceName; p.DisplayName="../Zuhause"; Assert.Equal(name,p.HomeServiceName); }
    [Fact] public void ServiceAclMinimal() { var sd=new RawSecurityDescriptor(TunnelIdentity.ServiceAcl("S-1-5-21-1-2-3-1001")); var ace=(CommonAce)sd.DiscretionaryAcl![2]; Assert.Equal(0xB4,ace.AccessMask); Assert.Equal(0,ace.AccessMask & (0x2|0x40000|0x80000|0x10000)); }
    [Fact] public void MachineDpapiRoundTrip() { var text=Config(); var protectedBytes=MachineSecrets.Protect(text,"test-name"); Assert.DoesNotContain(text,System.Text.Encoding.UTF8.GetString(protectedBytes)); var result=MachineSecrets.Unprotect(protectedBytes); try { Assert.Equal("test-name",result.Name); Assert.Equal(text,System.Text.Encoding.UTF8.GetString(result.Payload)); } finally { CryptographicOperations.ZeroMemory(result.Payload); } }
    [Fact] public void DpapiTamperingFails() { var bytes=MachineSecrets.Protect(Config(),"test"); bytes[^5]^=0xff; Assert.ThrowsAny<Exception>(()=>MachineSecrets.Unprotect(bytes)); }
    [Fact] public void MetadataHasNoSecrets() { var json=JsonSerializer.Serialize(new AppSettings { Profiles=[Profile("10.0.0.0/8")] }); Assert.DoesNotContain("PrivateKey",json); Assert.DoesNotContain("PresharedKey",json); Assert.DoesNotContain("Endpoint",json); }
    [Fact] public void LegacyJsonRemainsLegacyAndUntouched() { var p=JsonSerializer.Deserialize<VpnProfile>("{\"HomeTunnelName\":\"Home\"}")!; var settings=new AppSettings { Profiles=[p] }; var plan=PolicyPlanner.Evaluate(settings,Online(),new Dictionary<Guid,string>()).Single(); Assert.False(plan.ShouldRun); Assert.Equal(PolicyReason.MigrationRequired,plan.Reason); Assert.Equal("WireGuardTunnel$Home",p.HomeServiceName); }
    public static VpnProfile Profile(string cidr) => new() { Backend=TunnelBackendKind.EmbeddedWireGuard, DesiredVpnEnabled=true, HomeCidrs=[cidr] };
    public static NetworkSnapshot Online(string session="a") => new() { Fingerprint=session,HasUsableNetwork=true,NameCandidates=["Office"] };
    [Fact] public void DesiredOffWins() { var p=Profile("10.0.0.0/8"); p.DesiredVpnEnabled=false; Assert.Equal(PolicyReason.UserOff,Evaluate(p).Reason); }
    [Fact] public void NoNetworkOff() { var p=Profile("10.0.0.0/8"); Assert.Equal(PolicyReason.NoNetwork,PolicyPlanner.Evaluate(new AppSettings { Profiles=[p] },NetworkSnapshot.Empty,new Dictionary<Guid,string>()).Single().Reason); }
    private static ProfilePlan Evaluate(VpnProfile p) => PolicyPlanner.Evaluate(new AppSettings { Profiles=[p] },Online(),new Dictionary<Guid,string>()).Single();
    [Fact] public void ExclusionAndSessionOverride()
    {
        var p=Profile("10.0.0.0/8"); var settings=new AppSettings { Profiles=[p],ExcludedNetworks=[new() { Name="Office",NetworkNamePattern="Office" }] };
        Assert.False(PolicyPlanner.Evaluate(settings,Online(),new Dictionary<Guid,string>()).Single().ShouldRun);
        var manual=new Dictionary<Guid,string> { [p.Id]="a" };
        Assert.True(PolicyPlanner.Evaluate(settings,Online(),manual).Single().ShouldRun);
        Assert.False(PolicyPlanner.Evaluate(settings,Online("b"),manual).Single().ShouldRun);
        settings.ExcludedNetworks[0].AllowManualOverride=false;
        Assert.False(PolicyPlanner.Evaluate(settings,Online(),manual).Single().ShouldRun);
    }
    [Theory][InlineData("10.0.0.0/8","10.1.0.0/16",1)][InlineData("10.0.0.0/8","192.168.0.0/16",2)][InlineData("fd00::/8","10.0.0.0/8",2)]
    public void ParallelSplit(string a,string b,int count) { var plans=PolicyPlanner.Evaluate(new AppSettings { Profiles=[Profile(a),Profile(b)] },Online(),new Dictionary<Guid,string>()); Assert.Equal(count,plans.Count(x=>x.ShouldRun)); Assert.All(plans,x=>Assert.True(x.Profile.DesiredVpnEnabled)); }
    [Fact] public void FullExclusiveAndPrimaryWins() { var a=Profile("10.0.0.0/8"); var b=Profile("192.168.0.0/16"); a.RoutingMode=b.RoutingMode=RoutingMode.FullTunnel; var plans=PolicyPlanner.Evaluate(new AppSettings { Profiles=[a,b],PrimaryProfileId=b.Id },Online(),new Dictionary<Guid,string>()); Assert.Equal(b.Id,plans.Single(x=>x.ShouldRun).Profile.Id); }
    [Fact] public void FullExcludesSplit() { var a=Profile("10.0.0.0/8"); a.RoutingMode=RoutingMode.FullTunnel; var plans=PolicyPlanner.Evaluate(new AppSettings { Profiles=[Profile("192.168.0.0/16"),a] },Online(),new Dictionary<Guid,string>()); Assert.Equal(a.Id,plans.Single(x=>x.ShouldRun).Profile.Id); }
    [Fact] public void ProfileScope() { var a=Profile("10.0.0.0/8"); var b=Profile("192.168.0.0/16"); var settings=new AppSettings { Profiles=[a,b],ExcludedNetworks=[new() { NetworkNamePattern="Office",ProfileIds=[a.Id] }] }; Assert.Equal(b.Id,PolicyPlanner.Evaluate(settings,Online(),new Dictionary<Guid,string>()).Single(x=>x.ShouldRun).Profile.Id); }
}
