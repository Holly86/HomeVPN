using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using HomeVpn.Models;
namespace HomeVpn.Infrastructure;

public sealed class ProfileInstaller(InstallationService installation)
{
    public TunnelTestResult? LastTest { get; private set; }
    public async Task<VpnProfile> InstallAsync(WireGuardConfig config, string displayName, string requestedTunnelName, IReadOnlyList<string> homeCidrs, VpnProfile? oldProfile, CancellationToken cancellationToken = default, SplitDnsSettings? splitDns = null)
    {
        var result = await ExecuteAsync(new SetupRequest("import", config.CanonicalText(), displayName, homeCidrs.ToArray(), SplitDns: splitDns), cancellationToken);
        LastTest = result.Test;
        return result.Profile ?? throw new InvalidDataException("Setup did not return a profile.");
    }
    public Task<SetupResponse> RemoveAsync(Guid id) => ExecuteAsync(new SetupRequest("remove", ProfileId: id));
    public Task<SetupResponse> TestAsync(Guid id) => ExecuteAsync(new SetupRequest("test", ProfileId: id));
    public Task<SetupResponse> ConfigureDnsAsync(Guid id, SplitDnsSettings settings) => ExecuteAsync(new SetupRequest("dns", ProfileId: id, SplitDns: settings));
    private async Task<SetupResponse> ExecuteAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(installation.InstalledExecutablePath)) throw new InvalidOperationException("Bitte HomeVPN zuerst mit HomeVPN Setup installieren.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var pipeName = "HomeVPN.Setup." + Guid.NewGuid().ToString("N");
        await using var pipe = SetupPipe.Create(pipeName);
        var start = new ProcessStartInfo(installation.InstalledExecutablePath) { UseShellExecute = true, Verb = "runas", Arguments = "--admin-install " + pipeName };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Setup konnte nicht gestartet werden.");
        var connected = pipe.WaitForConnectionAsync(timeout.Token);
        var exited = process.WaitForExitAsync(timeout.Token);
        if (await Task.WhenAny(connected, exited) == exited && !pipe.IsConnected) throw new InvalidOperationException("Setup wurde vorzeitig beendet.");
        await connected;
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != process.Id) throw new UnauthorizedAccessException("Unexpected setup client.");
        await SetupProtocol.SendAsync(pipe, request, timeout.Token);
        var response = await SetupProtocol.ReceiveAsync<SetupResponse>(pipe, timeout.Token);
        if (!response.Success) throw new InvalidOperationException(response.Error ?? "Einrichtung fehlgeschlagen.");
        return response;
    }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
}
