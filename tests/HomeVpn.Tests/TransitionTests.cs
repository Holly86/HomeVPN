using HomeVpn.Infrastructure;
using HomeVpn.Models;
using HomeVpn.Services;
using Xunit;
namespace HomeVpn.Tests;
public sealed class TransitionTests
{
    private sealed class Controller : ITunnelController
    {
        public List<string> Calls { get; } = [];
        public bool FailStop { get; init; }
        public ServiceSnapshot Query(string name) => new() { Name=name,State=WindowsServiceState.Stopped };
        public Task StartAsync(string name,CancellationToken token=default) { Calls.Add("start:"+name); return Task.CompletedTask; }
        public Task StopAsync(string name,CancellationToken token=default) { Calls.Add("stop:"+name); if(FailStop) throw new IOException(); return Task.CompletedTask; }
    }
    [Fact] public async Task StopAllBeforeStartingWinner()
    {
        var a=RuntimeTests.Profile("10.0.0.0/8"); var b=RuntimeTests.Profile("192.168.0.0/16"); a.RoutingMode=RoutingMode.FullTunnel;
        var plans=PolicyPlanner.Evaluate(new AppSettings { Profiles=[a,b] },RuntimeTests.Online(),new Dictionary<Guid,string>());
        var controller=new Controller(); await PolicyTransition.ApplyAsync(plans,controller);
        Assert.StartsWith("start:",controller.Calls.Last()); Assert.All(controller.Calls.SkipLast(1),x=>Assert.StartsWith("stop:",x));
        Assert.Contains("stop:"+b.HomeServiceName,controller.Calls); Assert.Contains("stop:"+a.HomeServiceName,controller.Calls);
    }
    [Fact] public async Task StopFailurePreventsAllStarts()
    {
        var a=RuntimeTests.Profile("10.0.0.0/8"); var controller=new Controller { FailStop=true };
        var plans=PolicyPlanner.Evaluate(new AppSettings { Profiles=[a] },RuntimeTests.Online(),new Dictionary<Guid,string>());
        var errors=await PolicyTransition.ApplyAsync(plans,controller); Assert.NotEmpty(errors); Assert.DoesNotContain(controller.Calls,x=>x.StartsWith("start:")); Assert.True(a.DesiredVpnEnabled);
    }
    [Fact] public async Task LegacyAndForeignNamesNeverTouched()
    {
        var old=new VpnProfile { HomeTunnelName="Home",FullTunnelName="Personal",DesiredVpnEnabled=true };
        var plans=PolicyPlanner.Evaluate(new AppSettings { Profiles=[old] },RuntimeTests.Online(),new Dictionary<Guid,string>());
        var controller=new Controller(); await PolicyTransition.ApplyAsync(plans,controller); Assert.Empty(controller.Calls);
    }
}
