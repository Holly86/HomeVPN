using System.Runtime.InteropServices;
using System.Security.Principal;
using HomeVpn.Infrastructure;
using HomeVpn.Models;

try
{
    if (args.Length == 3 && args[0] == "--dns-watch" && Guid.TryParseExact(args[1], "N", out var dnsId) && int.TryParse(args[2], out var parentId))
    {
        using var dnsIdentity = WindowsIdentity.GetCurrent();
        if (!dnsIdentity.IsSystem || !string.Equals(Environment.ProcessPath, NativeRuntime.HostPath, StringComparison.OrdinalIgnoreCase)) return 5;
        await SplitDnsRuntime.WatchAsync(dnsId, parentId);
        return 0;
    }
    if (args.Length == 2 && args[0] == "--user-cleanup")
    {
        // MSI runs this action impersonating its initiating user. HKCU must never resolve to SYSTEM here.
        using var user = WindowsIdentity.GetCurrent();
        if (user.IsSystem || user.User?.Value != args[1]) return 5;
        using var userRun = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        var expected = '"' + Path.Combine(NativeRuntime.InstallRoot, "HomeVPN.exe") + '"';
        if (userRun?.GetValue("HomeVPN") is string command && command.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
            userRun.DeleteValue("HomeVPN", false);
        return 0;
    }
    if (args.Length == 2 && args[0] == "--maintenance" && args[1] is "remove" or "purge" or "restore" or "uninstall")
    {
        using var current = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(current).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
        await InstallationMaintenance.RunAsync(args[1]);
        return 0;
    }
    if (args.Length != 3 || args[0] != "--service" || !Guid.TryParseExact(args[1], "N", out var id) || args[2] is not ("split" or "full")) return 2;
    using var identity = WindowsIdentity.GetCurrent();
    if (!identity.IsSystem) return 5;
    if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), NativeRuntime.HostPath, StringComparison.OrdinalIgnoreCase)) return 5;
    var mode = args[2] == "split" ? RoutingMode.HomeOnly : RoutingMode.FullTunnel;
    if (!EmbeddedProvisioner.IsOwned(id, mode)) return 5;
    var path = Path.Combine(MachineSecrets.ProfileDirectory(id), TunnelIdentity.Name(id, mode) + ".conf.dpapi");
    MachineSecrets.RejectReparsePoints(path);
    MachineSecrets.VerifyDirectory(MachineSecrets.ProfileDirectory(id));
    var decrypted = MachineSecrets.Unprotect(File.ReadAllBytes(path));
    try
    {
        if (decrypted.Name != TunnelIdentity.Name(id, mode)) return 5;
        _ = WireGuardConfig.ParseText(System.Text.Encoding.UTF8.GetString(decrypted.Payload));
    }
    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(decrypted.Payload); }
    if (mode == RoutingMode.FullTunnel) await SplitDnsRuntime.RemoveAsync(id);
    using var dnsCompanion = mode == RoutingMode.HomeOnly ? SplitDnsRuntime.StartCompanion(id) : null;
    var library = NativeRuntime.Load("tunnel.dll");
    var run = Marshal.GetDelegateForFunctionPointer<TunnelService>(NativeLibrary.GetExport(library, "WireGuardTunnelService"));
    // Upstream reads and decrypts the protected file itself; no plaintext materialization, ever.
    try { return run(path) ? 0 : 1; }
    finally
    {
        if (dnsCompanion is not null)
        {
            dnsCompanion.StandardInput.Close();
            await dnsCompanion.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
catch { return 1; } // do not send configuration, native errors or keys to stdout/crash logs

[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet=CharSet.Unicode)]
[return: MarshalAs(UnmanagedType.U1)]
delegate bool TunnelService([MarshalAs(UnmanagedType.LPWStr)] string protectedConfigurationPath);
