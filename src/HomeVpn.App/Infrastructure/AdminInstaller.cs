using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeVpn.Models;
namespace HomeVpn.Infrastructure;

public static class AdminInstaller
{
    public static bool IsAdministrator() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    public static async Task<int> RunAsync(string pipeName)
    {
        if (!IsAdministrator() || !Regex.IsMatch(pipeName, "^HomeVPN[.]Setup[.][a-f0-9]{32}$")) return 5;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(timeout.Token);
            SetupPipe.VerifyServerOwner(pipe);
            var request = await SetupProtocol.ReceiveAsync<SetupRequest>(pipe, timeout.Token);
            using var identity = WindowsIdentity.GetCurrent();
            var provisioner = new EmbeddedProvisioner();
            VpnProfile? profile = null; TunnelTestResult? test = null;
            if (request.Operation == "import")
            {
                var dns = SplitDns.Normalize(request.SplitDns, request.Routes ?? []);
                profile = await provisioner.ProvisionAsync(request.Configuration ?? "", request.DisplayName ?? "", request.Routes ?? [], identity.User!.Value);
                MachineSecrets.WriteAtomic(Path.Combine(MachineSecrets.ProfileDirectory(profile.Id), "profile.json"), JsonSerializer.SerializeToUtf8Bytes(profile));
                if (dns.Enabled) await SplitDnsRuntime.ConfigureAsync(profile, dns);
                test = await SafeTestAsync(profile);
            }
            else if (request.Operation is "remove" or "test" or "dns" && request.ProfileId is Guid id)
            {
                if (!EmbeddedProvisioner.IsOwned(id, RoutingMode.HomeOnly) || !EmbeddedProvisioner.IsOwned(id, RoutingMode.FullTunnel)) throw new UnauthorizedAccessException();
                var directory = MachineSecrets.ProfileDirectory(id);
                var owner = JsonSerializer.Deserialize<EmbeddedProvisioner.Ownership>(File.ReadAllText(Path.Combine(directory, "owner.json")));
                if (owner?.UserSid != identity.User!.Value) throw new UnauthorizedAccessException();
                if (request.Operation == "dns")
                {
                    profile = JsonSerializer.Deserialize<VpnProfile>(File.ReadAllText(Path.Combine(directory, "profile.json")))!;
                    var dns = SplitDns.Normalize(request.SplitDns, profile.HomeCidrs);
                    // Only protected metadata is changed. Keys and existing tunnel configurations stay untouched.
                    await SplitDnsRuntime.ConfigureAsync(profile, dns);
                }
                else if (request.Operation == "remove") await provisioner.RemoveAsync(id, true);
                else test = await SafeTestAsync(JsonSerializer.Deserialize<VpnProfile>(File.ReadAllText(Path.Combine(directory, "profile.json")))!);
            }
            else throw new InvalidDataException();
            await SetupProtocol.SendAsync(pipe, new SetupResponse(true, profile, test), timeout.Token);
            return 0;
        }
        catch (Exception ex)
        {
            // Never echo request/configuration/parser input into UI or logs.
            var message = ex is UnauthorizedAccessException ? "Fehlende Rechte oder nicht bestätigte HomeVPN-Service-Eigentümerschaft."
                : ex is InvalidDataException ? "Konfiguration oder eingebettete Runtime ungültig."
                : $"Die Einrichtung konnte nicht abgeschlossen werden ({ex.GetType().Name}).";
            if (pipe.IsConnected) try { await SetupProtocol.SendAsync(pipe, new SetupResponse(false, Error: message), timeout.Token); } catch { }
            return 1;
        }
    }
    private static async Task<TunnelTestResult> SafeTestAsync(VpnProfile profile)
    {
        if (System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Any(x => x.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && x.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)))
            return new TunnelTestResult(true, false, false, false, null, 0, 0, "Test pausiert: Ein anderer WireGuard-Tunnel ist aktiv. Diesen bitte selbst trennen und erneut testen.");
        try { return await TunnelDiagnostics.TestSplitAsync(profile); }
        catch { return new TunnelTestResult(true, false, false, false, null, 0, 0, "Test konnte nicht sicher beendet werden. HomeVPN prüft und stoppt den Dienst erneut; bis dahin keine weitere Verbindung aktivieren."); }
    }
}
