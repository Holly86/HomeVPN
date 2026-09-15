using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using HomeVpn.Infrastructure;
using HomeVpn.Models;
using Xunit;

namespace HomeVpn.Tests;

public class PersistenceAndPipeTests
{
    [Fact]
    public void MachinePurgeInvalidatesUserMetadataButUpgradeRetainsIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HomeVPN-tests", Guid.NewGuid().ToString("N"));
        string? generation = null;
        try
        {
            var store = new SettingsStore(directory, () => generation);
            var profile = RuntimeTests.Profile("10.0.0.0/24");
            store.Save(new AppSettings { Profiles=[profile], ExcludedNetworks=[new() {Name="Office"}] });
            Assert.Equal(profile.Id, store.Load().Profiles.Single().Id);
            generation = Guid.NewGuid().ToString("N");
            var reset = store.Load();
            Assert.Empty(reset.Profiles);
            Assert.Empty(reset.ExcludedNetworks);
            Assert.Equal(generation, reset.SettingsGeneration);
            Assert.DoesNotContain(profile.Id.ToString(), File.ReadAllText(store.SettingsPath));
            store.Save(new AppSettings { Profiles=[profile] });
            Assert.Equal(profile.Id, store.Load().Profiles.Single().Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Profiles\":null}")]
    public void CorruptSettingsAreNotSilentlyReplaced(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HomeVPN-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(directory); File.WriteAllText(store.SettingsPath, content);
            Assert.Throws<InvalidDataException>(() => store.Load());
            Assert.Equal(content, File.ReadAllText(store.SettingsPath));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SetupPipeUsesAccountSidAndTransportsBoundedFrames()
    {
        var name = "HomeVPN.Test." + Guid.NewGuid().ToString("N");
        await using var server = SetupPipe.Create(name);
        using var identity = WindowsIdentity.GetCurrent();
        Assert.Equal(identity.User, server.GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
        Assert.All(server.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>(),
            rule => Assert.Equal(identity.User, rule.IdentityReference));
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = server.WaitForConnectionAsync(token.Token);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(token.Token); await connected;
        SetupPipe.VerifyServerOwner(client);
        var received = SetupProtocol.ReceiveAsync<SetupRequest>(server, token.Token);
        await SetupProtocol.SendAsync(client, new SetupRequest("test", ProfileId: Guid.NewGuid()), token.Token);
        Assert.Equal("test", (await received).Operation);
        var rejected = Assert.ThrowsAsync<InvalidDataException>(() => SetupProtocol.ReceiveAsync<SetupRequest>(server, token.Token));
        await client.WriteAsync(BitConverter.GetBytes(131073), token.Token);
        await rejected;
    }

    [Fact]
    public void PersistedDesiredStateAndRoutingSurviveReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HomeVPN-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var profile = RuntimeTests.Profile("10.0.0.0/24");
            profile.RoutingMode = RoutingMode.FullTunnel;
            store.Save(new AppSettings { Profiles = [profile], PrimaryProfileId = profile.Id });
            var loaded = store.Load();
            Assert.True(loaded.Profiles.Single().DesiredVpnEnabled);
            Assert.Equal(RoutingMode.FullTunnel, loaded.Profiles.Single().RoutingMode);
            Assert.Equal(profile.Id, loaded.PrimaryProfileId);
            Assert.DoesNotContain("Override", File.ReadAllText(store.SettingsPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
