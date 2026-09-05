using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HomeVpn.Infrastructure;

public static class NativeRuntime
{
    public static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HomeVPN");
    public static string DirectoryPath => Path.Combine(InstallRoot, "Runtime", "x64");
    public static string HostPath => Path.Combine(DirectoryPath, "HomeVPN.TunnelService.exe");
    public static void Verify(string? directory = null)
    {
        directory ??= DirectoryPath;
        MachineSecrets.RejectReparsePoints(directory);
        var pins = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(directory, "native-hashes.json")))!;
        foreach (var name in new[] { "tunnel.dll", "wireguard.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(directory, name));
            if (!pins.TryGetValue(name, out var expected) || !Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded Runtime hash verification failed.");
        }
    }
    public static IntPtr Load(string name, string? directory = null)
    {
        directory ??= DirectoryPath;
        Verify(directory);
        if (name is not ("tunnel.dll" or "wireguard.dll")) throw new ArgumentException("Unknown runtime component.");
        // No working directory/PATH/user directories. Upstream also uses APPLICATION_DIR|SYSTEM32.
        if (!SetDefaultDllDirectories(0xA00)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var handle = LoadLibraryEx(Path.Combine(directory, name), IntPtr.Zero, 0x900);
        return handle != IntPtr.Zero ? handle : throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
}
