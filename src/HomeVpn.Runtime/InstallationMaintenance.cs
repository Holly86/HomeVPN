using System.Text.Json;
using HomeVpn.Models;
using Microsoft.Win32;
namespace HomeVpn.Infrastructure;
public static class InstallationMaintenance
{
    public static async Task RunAsync(string operation)
    {
        if (operation is not ("remove" or "purge" or "restore" or "uninstall")) throw new ArgumentException("Invalid maintenance operation.");
        if (operation is "uninstall" or "purge") RemoveAutostart();
        if (operation == "uninstall") return;
        var profiles = Path.Combine(MachineSecrets.Root, "Profiles");
        MachineSecrets.RejectReparsePoints(profiles);
        if (!Directory.Exists(profiles))
        {
            if (operation == "purge") SettingsGeneration.Reset();
            return;
        }
        MachineSecrets.VerifyDirectory(MachineSecrets.Root);
        MachineSecrets.VerifyDirectory(profiles);
        foreach (var directory in Directory.EnumerateDirectories(profiles))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            MachineSecrets.VerifyDirectory(directory);
            var record = Path.Combine(directory, "owner.json");
            if (!File.Exists(record)) continue;
            var owner = JsonSerializer.Deserialize<EmbeddedProvisioner.Ownership>(File.ReadAllText(record));
            if (owner?.Id != id || owner.Host != NativeRuntime.HostPath) continue;
            if (operation is "remove" or "purge") await new EmbeddedProvisioner().RemoveAsync(id, operation == "purge");
            else if (operation == "restore")
            {
                NativeRuntime.Verify();
                foreach (var mode in new[] { RoutingMode.HomeOnly, RoutingMode.FullTunnel })
                {
                    var name = TunnelIdentity.Service(id, mode);
                    if (new WindowsServiceManager().Query(name).State != WindowsServiceState.NotFound)
                    { if (EmbeddedProvisioner.IsOwned(id, mode)) continue; throw new InvalidOperationException("Existing service prevents restore."); }
                    ServiceProvisioning.Create(name, "HomeVPN – " + id.ToString("N"), EmbeddedProvisioner.BinaryPath(id, mode), owner.UserSid);
                }
            }
            else throw new ArgumentException("Invalid maintenance operation.");
        }
        if (operation == "purge") SettingsGeneration.Reset();
    }
    private static void RemoveAutostart()
    {
            foreach (var sid in Registry.Users.GetSubKeyNames())
            {
                using var run = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (run?.GetValue("HomeVPN") is string value && value.StartsWith('"' + Path.Combine(NativeRuntime.InstallRoot,"HomeVPN.exe") + '"', StringComparison.OrdinalIgnoreCase)) run.DeleteValue("HomeVPN", false);
            }
    }
}
