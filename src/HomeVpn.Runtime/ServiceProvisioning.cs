using System.ComponentModel;
using System.Runtime.InteropServices;
namespace HomeVpn.Infrastructure;

public static class ServiceProvisioning
{
    public static void Create(string name, string displayName, string binaryPath, string sid)
    {
        var scm = OpenSCManager(null, null, 3);
        if (scm == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var service = CreateService(scm, name, displayName, 0xF01FF, 0x10, 3, 1, binaryPath, null, IntPtr.Zero, "Nsi\0TcpIp\0\0", null, null);
            if (service == IntPtr.Zero) throw new Win32Exception(); // never overwrite existing service
            try
            {
                uint unrestricted = 1;
                if (!ChangeServiceConfig2(service, 5, ref unrestricted)) throw new Win32Exception();
                if (!ConvertStringSecurityDescriptorToSecurityDescriptor(TunnelIdentity.ServiceAcl(sid), 1, out var descriptor, out _)) throw new Win32Exception();
                try { if (!SetServiceObjectSecurity(service, 4, descriptor)) throw new Win32Exception(); }
                finally { LocalFree(descriptor); }
            }
            catch { DeleteService(service); throw; }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }
    public static string? GetBinaryPath(string name)
    {
        var scm = OpenSCManager(null, null, 1);
        if (scm == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var service = OpenService(scm, name, 1);
            if (service == IntPtr.Zero) { if (Marshal.GetLastWin32Error() == 1060) return null; throw new Win32Exception(); }
            try
            {
                QueryServiceConfig(service, IntPtr.Zero, 0, out var size);
                var buffer = Marshal.AllocHGlobal((int)size);
                try { if (!QueryServiceConfig(service, buffer, size, out _)) throw new Win32Exception(); return Marshal.PtrToStructure<Config>(buffer).BinaryPath; }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }
    public static async Task DeleteAsync(string name)
    {
        await new WindowsServiceManager().StopAsync(name);
        var scm = OpenSCManager(null, null, 1);
        if (scm == IntPtr.Zero) throw new Win32Exception();
        try { var service = OpenService(scm, name, 0x10000); if (service == IntPtr.Zero) { if (Marshal.GetLastWin32Error() == 1060) return; throw new Win32Exception(); }
            try { if (!DeleteService(service)) throw new Win32Exception(); } finally { CloseServiceHandle(service); } }
        finally { CloseServiceHandle(scm); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Config { public uint Type, Start, Error; public string BinaryPath; public string Group; public uint Tag; public string Dependencies, Account, Display; }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr OpenService(IntPtr scm,string name,uint access);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateService(IntPtr scm,string name,string display,uint access,uint type,uint start,uint error,string binary,string? group,IntPtr tag,string dependencies,string? user,string? password);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool ChangeServiceConfig2(IntPtr service,uint level,ref uint info);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool QueryServiceConfig(IntPtr service,IntPtr config,uint size,out uint needed);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool SetServiceObjectSecurity(IntPtr service,uint info,IntPtr descriptor);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr service);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
