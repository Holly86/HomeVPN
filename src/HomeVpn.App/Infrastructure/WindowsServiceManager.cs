using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public sealed class WindowsServiceManager : ITunnelController
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceControlStop = 0x00000001;
    private const int ScStatusProcessInfo = 0;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;

    public ServiceSnapshot Query(string serviceName)
    {
        var scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var service = OpenService(scm, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorServiceDoesNotExist)
                    return new ServiceSnapshot { Name = serviceName, State = WindowsServiceState.NotFound };
                throw new Win32Exception(error);
            }

            try
            {
                var status = QueryStatus(service);
                DateTimeOffset? startedAt = null;
                if (status.dwProcessId != 0 && status.dwCurrentState == (uint)WindowsServiceState.Running)
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)status.dwProcessId);
                        startedAt = process.StartTime;
                    }
                    catch
                    {
                        // Process start time is cosmetic; service state is authoritative.
                    }
                }

                return new ServiceSnapshot
                {
                    Name = serviceName,
                    State = Enum.IsDefined(typeof(WindowsServiceState), (int)status.dwCurrentState)
                        ? (WindowsServiceState)status.dwCurrentState
                        : WindowsServiceState.Unknown,
                    ProcessId = status.dwProcessId,
                    ProcessStartedAt = startedAt
                };
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public async Task StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var service = OpenService(scm, serviceName, ServiceStart | ServiceQueryStatus);
            if (service == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (!StartService(service, 0, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorServiceAlreadyRunning)
                        throw new Win32Exception(error);
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }

        await WaitForStateAsync(serviceName, WindowsServiceState.Running, TimeSpan.FromSeconds(12), cancellationToken);
    }

    public async Task StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var current = Query(serviceName);
        if (current.State is WindowsServiceState.NotFound or WindowsServiceState.Stopped)
            return;
        if (current.State == WindowsServiceState.StopPending)
        {
            await WaitForStateAsync(serviceName, WindowsServiceState.Stopped, TimeSpan.FromSeconds(12), cancellationToken);
            return;
        }

        var scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var service = OpenService(scm, serviceName, ServiceStop | ServiceQueryStatus);
            if (service == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var status = new SERVICE_STATUS();
                if (!ControlService(service, ServiceControlStop, ref status))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorServiceNotActive)
                        throw new Win32Exception(error);
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }

        await WaitForStateAsync(serviceName, WindowsServiceState.Stopped, TimeSpan.FromSeconds(12), cancellationToken);
    }

    private async Task WaitForStateAsync(
        string serviceName,
        WindowsServiceState expected,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = Query(serviceName);
            if (snapshot.State == expected ||
                (expected == WindowsServiceState.Stopped && snapshot.State == WindowsServiceState.NotFound))
                return;

            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException($"Service '{serviceName}' did not reach state {expected} within {timeout.TotalSeconds:0} seconds.");
    }

    private static SERVICE_STATUS_PROCESS QueryStatus(IntPtr service)
    {
        var size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, int numServiceArgs, IntPtr serviceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        IntPtr buffer,
        int bufferSize,
        out int bytesNeeded);
}
