using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HomeVpn.Infrastructure;

public static class SetupPipe
{
    public static NamedPipeServerStream Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!;
        var acl = new PipeSecurity();
        acl.SetOwner(sid);
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, acl);
    }

    public static void VerifyServerOwner(NamedPipeClientStream pipe)
    {
        // .NET 8 CurrentUserOnly compares TokenOwner (Administrators after UAC),
        // not TokenUser. Use the actual account SID across the elevation boundary.
        using var identity = WindowsIdentity.GetCurrent();
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (!identity.User!.Equals(owner)) throw new UnauthorizedAccessException("Unexpected setup pipe owner.");
    }
}
