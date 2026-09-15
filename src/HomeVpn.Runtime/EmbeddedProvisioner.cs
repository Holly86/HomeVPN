using System.Text.Json;
using HomeVpn.Models;
namespace HomeVpn.Infrastructure;

public sealed class EmbeddedProvisioner : ITunnelProvisioner
{
    public async Task<VpnProfile> ProvisionAsync(string configuration, string displayName, IReadOnlyList<string> routes, string userSid)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80 || displayName.Any(char.IsControl)) throw new InvalidDataException("Bitte einen Namen mit 1–80 Zeichen wählen.");
        NativeRuntime.Verify();
        var config = WireGuardConfig.ParseText(configuration);
        var split = config.CreateHomeOnlyVariant(routes); var full = config.CreateFullTunnelVariant();
        var id = Guid.NewGuid();
        var profile = new VpnProfile { Id = id, DisplayName = displayName.Trim(), HomeCidrs = WireGuardConfig.ParseText(split).AllowedIps.ToList(),
            HomeTunnelName = TunnelIdentity.Name(id, RoutingMode.HomeOnly), FullTunnelName = TunnelIdentity.Name(id, RoutingMode.FullTunnel), Backend = TunnelBackendKind.EmbeddedWireGuard };
        MachineSecrets.EnsureDirectory(MachineSecrets.Root);
        MachineSecrets.EnsureDirectory(Path.Combine(MachineSecrets.Root, "Profiles"));
        var directory = MachineSecrets.ProfileDirectory(id);
        MachineSecrets.EnsureDirectory(directory);
        var created = new List<string>();
        try
        {
            foreach (var mode in new[] { RoutingMode.HomeOnly, RoutingMode.FullTunnel })
            {
                var name = TunnelIdentity.Name(id, mode);
                MachineSecrets.WriteAtomic(Path.Combine(directory, name + ".conf.dpapi"), MachineSecrets.Protect(mode == RoutingMode.HomeOnly ? split : full, name));
                var service = TunnelIdentity.Service(id, mode);
                ServiceProvisioning.Create(service, $"HomeVPN – {displayName} – {(mode == RoutingMode.HomeOnly ? "Nur Heimnetz" : "Gesamter Verkehr")}", BinaryPath(id, mode), userSid);
                created.Add(service);
            }
            MachineSecrets.WriteAtomic(Path.Combine(directory, "owner.json"), JsonSerializer.SerializeToUtf8Bytes(new Ownership(id, userSid, NativeRuntime.HostPath)));
            return profile;
        }
        catch
        {
            foreach (var service in created) await ServiceProvisioning.DeleteAsync(service);
            MachineSecrets.RejectReparsePoints(directory);
            Directory.Delete(directory, true);
            throw;
        }
    }
    public static string BinaryPath(Guid id, RoutingMode mode) => $"\"{NativeRuntime.HostPath}\" --service {id:N} {(mode == RoutingMode.HomeOnly ? "split" : "full")}";
    public static bool IsOwned(Guid id, RoutingMode mode)
    {
        var path = Path.Combine(MachineSecrets.ProfileDirectory(id), "owner.json");
        MachineSecrets.RejectReparsePoints(path);
        if (!File.Exists(path)) return false;
        MachineSecrets.VerifyDirectory(MachineSecrets.Root);
        MachineSecrets.VerifyDirectory(Path.GetDirectoryName(path)!);
        var owner = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(path));
        return owner?.Id == id && owner.Host == NativeRuntime.HostPath && ServiceProvisioning.GetBinaryPath(TunnelIdentity.Service(id, mode)) == BinaryPath(id, mode);
    }
    public async Task RemoveAsync(Guid id, bool deleteSecrets)
    {
        var directory = MachineSecrets.ProfileDirectory(id);
        MachineSecrets.VerifyDirectory(directory);
        var owner = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(Path.Combine(directory, "owner.json")));
        if (owner?.Id != id || owner.Host != NativeRuntime.HostPath) throw new UnauthorizedAccessException("Profile ownership is missing.");
        foreach (var mode in new[] { RoutingMode.HomeOnly, RoutingMode.FullTunnel })
            if (IsOwned(id, mode)) await ServiceProvisioning.DeleteAsync(TunnelIdentity.Service(id, mode));
            else if (new WindowsServiceManager().Query(TunnelIdentity.Service(id, mode)).State != WindowsServiceState.NotFound)
                throw new InvalidOperationException("Service ownership could not be verified; no deletion performed.");
        await SplitDnsRuntime.RemoveAsync(id);
        if (deleteSecrets)
        {
            MachineSecrets.RejectReparsePoints(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    public sealed record Ownership(Guid Id, string UserSid, string Host);
}
