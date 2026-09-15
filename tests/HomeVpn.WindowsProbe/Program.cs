using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using HomeVpn.Infrastructure;
using HomeVpn.Models;

// Local acceptance probe only. Never included in the installer. No config/key input or output.
if (args.Length != 2 || !Guid.TryParseExact(args[1], "N", out var id) || args[0] is not ("rights" or "peer" or "audit" or "test" or "cleanup")) return 2;
using var identity = WindowsIdentity.GetCurrent();
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
if (args[0] is "audit" or "test" or "cleanup")
{
    if (!elevated) return 5;
    object result;
    if (args[0] == "test")
    {
        var profile = JsonSerializer.Deserialize<VpnProfile>(File.ReadAllText(Path.Combine(MachineSecrets.ProfileDirectory(id), "profile.json")))!;
        result = await TunnelDiagnostics.TestSplitAsync(profile);
    }
    else
    {
        if (args[0] == "cleanup") await InstallationMaintenance.RunAsync("uninstall");
        var entries = new List<object>();
        foreach (var sid in Microsoft.Win32.Registry.Users.GetSubKeyNames())
        {
            using var run = Microsoft.Win32.Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Run", false);
            var value = run?.GetValue("HomeVPN") as string;
            entries.Add(new { Present = value is not null, Matches = value?.StartsWith('"' + Path.Combine(NativeRuntime.InstallRoot,"HomeVPN.exe") + '"', StringComparison.OrdinalIgnoreCase) });
        }
        var profiles = Path.Combine(MachineSecrets.Root, "Profiles");
        result = new { NativeRuntime.InstallRoot, Entries=entries, ProfileCount=Directory.Exists(profiles) ? Directory.GetDirectories(profiles).Length : 0 };
    }
    await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, args[0] + "-result.json"), JsonSerializer.Serialize(result));
    return 0;
}
if (args[0] == "peer")
{
    if (!elevated) return 5;
    var mode = new WindowsServiceManager().Query(TunnelIdentity.Service(id, RoutingMode.FullTunnel)).IsRunning ? RoutingMode.FullTunnel : RoutingMode.HomeOnly;
    var peer = TunnelDiagnostics.ReadPeer(TunnelIdentity.Name(id, mode));
    Console.WriteLine(JsonSerializer.Serialize(new { peer.Handshake, peer.Rx, peer.Tx }));
    return peer.Handshake == true ? 0 : 1;
}
var scm = Native.OpenSCManager(null, null, 1);
if (scm == IntPtr.Zero) return 5;
bool passed = !elevated;
try
{
    Console.WriteLine("Elevated=" + elevated);
    foreach (var mode in new[] { RoutingMode.HomeOnly, RoutingMode.FullTunnel })
    {
        var name = TunnelIdentity.Service(id, mode);
        foreach (var (right, allowed) in new[] { (0x4u,true), (0x10u,true), (0x20u,true), (0x80u,true), (0x2u,false), (0x40000u,false), (0x10000u,false) })
        {
            var handle = Native.OpenService(scm, name, right);
            bool granted = handle != IntPtr.Zero;
            var error = granted ? 0 : Marshal.GetLastWin32Error();
            if (granted) Native.CloseServiceHandle(handle);
            bool ok = granted == allowed && (granted || error == 5);
            passed &= ok;
            Console.WriteLine($"{mode} right 0x{right:X}: {(ok ? "PASS" : "FAIL")}");
        }
    }
    bool protectedReadDenied = false;
    try { Directory.GetFiles(MachineSecrets.ProfileDirectory(id)); }
    catch (UnauthorizedAccessException) { protectedReadDenied = true; }
    Console.WriteLine("Protected store read denied=" + protectedReadDenied);
    passed &= protectedReadDenied;
}
finally { Native.CloseServiceHandle(scm); }
return passed ? 0 : 1;

static class Native
{
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr OpenService(IntPtr scm,string name,uint access);
    [DllImport("advapi32.dll")] public static extern bool CloseServiceHandle(IntPtr handle);
}
