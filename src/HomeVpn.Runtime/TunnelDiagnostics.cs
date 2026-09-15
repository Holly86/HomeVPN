using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using HomeVpn.Models;
namespace HomeVpn.Infrastructure;

public sealed record TunnelTestResult(bool Runtime, bool Service, bool Adapter, bool Routes, bool? Handshake, ulong Rx, ulong Tx, string Summary);
public static class TunnelDiagnostics
{
    public static async Task<TunnelTestResult> TestSplitAsync(VpnProfile profile)
    {
        var services = new WindowsServiceManager();
        bool running = false, adapter = false, routes = false, runtime = false;
        (bool? Handshake, ulong Rx, ulong Tx) peer = (null, 0, 0);
        string message;
        try
        {
            // Never stop foreign tunnels or test /0. Caller must suspend managed policy first.
            await services.StartAsync(profile.HomeServiceName);
            var status = services.Query(profile.HomeServiceName); running = status.IsRunning;
            using var process = Process.GetProcessById((int)status.ProcessId);
            runtime = new[] { "tunnel.dll", "wireguard.dll" }.All(name => process.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.FileName, Path.Combine(NativeRuntime.DirectoryPath, name), StringComparison.OrdinalIgnoreCase)));
            adapter = NetworkInterface.GetAllNetworkInterfaces().Any(x => x.Name == profile.HomeTunnelName && x.OperationalStatus == OperationalStatus.Up);
            // Get-NetRoute returns only booleans; arguments are fixed GUID-derived name and parsed CIDRs.
            var command = "$r = @(Get-NetRoute -InterfaceAlias '" + profile.HomeTunnelName + "' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DestinationPrefix); " +
                "@(" + string.Join(",", profile.HomeCidrs.Select(x => "'" + x + "'")) + ") | ForEach-Object { $r -contains $_ }";
            var result = await ProcessRunner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"), ["-NoProfile", "-NonInteractive", "-Command", command]);
            var values = result.StandardOutput.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries);
            routes = result.Success && values.Length == profile.HomeCidrs.Count && values.All(x => x == "True");
            // Generate a split-only probe; the peer may ignore ICMP while still handshaking.
            foreach (var cidr in profile.HomeCidrs.Take(1))
            {
                var address = HomeVpn.Core.Cidr.TryParse(cidr, out var c) ? c!.Network.GetAddressBytes() : [];
                if (address.Length == 4) { address[3] = (byte)(address[3] | 1); using var ping = new Ping(); try { await ping.SendPingAsync(new System.Net.IPAddress(address), 1500); } catch (PingException) { } }
            }
            await Task.Delay(2000);
            peer = ReadPeer(profile.HomeTunnelName);
            message = peer.Handshake == true ? "Peer-Handshake erfolgreich." : "Runtime geprüft; Peer momentan nicht bestätigt. Verbindung später erneut testen.";
        }
        catch (Exception ex) { message = "Verbindungstest nicht vollständig: " + ex.GetType().Name; }
        finally { await services.StopAsync(profile.HomeServiceName); }
        return new(runtime, running, adapter, routes, peer.Handshake, peer.Rx, peer.Tx, message);
    }
    public static (bool? Handshake, ulong Rx, ulong Tx) ReadPeer(string name)
    {
        var library = NativeRuntime.Load("wireguard.dll");
        var open = Marshal.GetDelegateForFunctionPointer<Open>(NativeLibrary.GetExport(library, "WireGuardOpenAdapter"));
        var close = Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "WireGuardCloseAdapter"));
        var get = Marshal.GetDelegateForFunctionPointer<Get>(NativeLibrary.GetExport(library, "WireGuardGetConfiguration"));
        var handle = open(name); if (handle == IntPtr.Zero) return (null, 0, 0);
        try
        {
            uint length = 0; get(handle, IntPtr.Zero, ref length);
            if (length < 216 || length > 1048576) return (null, 0, 0);
            var capacity = length;
            var buffer = Marshal.AllocHGlobal((int)capacity);
            try
            {
                if (!get(handle, buffer, ref length) || length < 216 || length > capacity || Marshal.ReadInt32(buffer, 72) < 1) return (null, 0, 0);
                // Upstream ABI: 80-byte interface, 136-byte peer, pack=8. Never marshal keys into managed objects.
                var peer = IntPtr.Add(buffer, 80);
                return (Marshal.ReadInt64(peer, 120) != 0, (ulong)Marshal.ReadInt64(peer, 112), (ulong)Marshal.ReadInt64(peer, 104));
            }
            finally { unsafe { new Span<byte>((void*)buffer, (int)capacity).Clear(); } Marshal.FreeHGlobal(buffer); }
        }
        finally { close(handle); }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Unicode)] private delegate IntPtr Open([MarshalAs(UnmanagedType.LPWStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Close(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool Get(IntPtr handle, IntPtr data, ref uint length);
}
