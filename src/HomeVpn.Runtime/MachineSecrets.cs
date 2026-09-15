using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace HomeVpn.Infrastructure;

public static class MachineSecrets
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HomeVPN");
    public static string ProfileDirectory(Guid id) => Path.Combine(Root, "Profiles", id.ToString("N"));

    public static void RejectReparsePoints(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse points are not allowed in HomeVPN protected paths.");
    }

    public static void EnsureDirectory(string path)
    {
        RejectReparsePoints(path);
        if (Directory.Exists(path)) VerifyDirectory(path);
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        // CreateDirectory with security sets the DACL at creation, before any payload is written.
        new DirectoryInfo(path).Create(acl);
        new DirectoryInfo(path).SetAccessControl(acl);
    }

    public static void VerifyDirectory(string path)
    {
        RejectReparsePoints(path);
        var acl = new DirectoryInfo(path).GetAccessControl();
        var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier ?? throw new UnauthorizedAccessException("Missing directory owner.");
        var allowed = new[] { "S-1-5-18", "S-1-5-32-544" };
        if (!allowed.Contains(owner.Value)) throw new UnauthorizedAccessException("Protected directory has an unexpected owner.");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains(rule.IdentityReference.Value))
                throw new UnauthorizedAccessException("Protected directory is accessible to other users.");
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        RejectReparsePoints(path);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static byte[] Protect(string configuration, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(configuration);
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var input = new Blob { Length = bytes.Length, Data = pin.AddrOfPinnedObject() };
        try
        {
            // Machine scope + description matching conf/dpapi.Decrypt; no optional entropy.
            if (!CryptProtectData(ref input, name, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x5, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var encrypted = new byte[output.Length]; Marshal.Copy(output.Data, encrypted, 0, encrypted.Length); return encrypted; }
            finally { ZeroMemory(output.Data, output.Length); LocalFree(output.Data); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); pin.Free(); }
    }
    public static (byte[] Payload, string Name) Unprotect(byte[] encrypted)
    {
        var pin = GCHandle.Alloc(encrypted, GCHandleType.Pinned);
        var input = new Blob { Length = encrypted.Length, Data = pin.AddrOfPinnedObject() };
        try
        {
            if (!CryptUnprotectData(ref input, out var name, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var bytes = new byte[output.Length]; Marshal.Copy(output.Data, bytes, 0, bytes.Length); return (bytes, Marshal.PtrToStringUni(name) ?? ""); }
            finally { ZeroMemory(output.Data, output.Length); LocalFree(output.Data); LocalFree(name); }
        }
        finally { pin.Free(); }
    }
    private static unsafe void ZeroMemory(IntPtr pointer, int length) => new Span<byte>((void*)pointer, length).Clear();
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob data, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob data, out IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
