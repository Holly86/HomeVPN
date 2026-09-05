using System.Security.Principal;
using System.Text.Json;
using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public static class AdminInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task<int> RunAsync(string manifestPath)
    {
        AdminInstallResult result;
        try
        {
            if (!IsAdministrator())
                throw new UnauthorizedAccessException("The installation step requires administrator rights.");

            var manifest = JsonSerializer.Deserialize<AdminInstallManifest>(await File.ReadAllTextAsync(manifestPath))
                           ?? throw new InvalidDataException("Invalid installation manifest.");

            var wireGuard = new WireGuardCli();
            var serviceManager = new WindowsServiceManager();
            var managerBefore = SafeQuery(serviceManager, "WireGuardManager");
            var managerExisted = managerBefore.State != WindowsServiceState.NotFound;
            var managerWasRunning = managerBefore.IsRunning;

            if (!managerExisted)
            {
                await wireGuard.InstallManagerAsync();
            }
            else if (!managerWasRunning)
            {
                await serviceManager.StartAsync("WireGuardManager");
            }

            await WaitForServiceRunningAsync(serviceManager, "WireGuardManager");

            try
            {
                Directory.CreateDirectory(wireGuard.SecureConfigurationDirectory);

                var tunnelsToRemove = manifest.OldTunnelNames
                    .Concat(new[] { manifest.HomeTunnelName, manifest.FullTunnelName })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var tunnel in tunnelsToRemove)
                {
                    await TryStopServiceAsync($"WireGuardTunnel${tunnel}");
                    await wireGuard.UninstallTunnelAsync(tunnel, ignoreFailure: true);
                    DeleteSecureConfigIfPresent(wireGuard.SecureConfigurationDirectory, tunnel);
                }

                var homeDpapi = await ImportIntoSecureStoreAsync(
                    wireGuard.SecureConfigurationDirectory,
                    manifest.HomeTunnelName,
                    manifest.HomeConfigPath);
                var fullDpapi = await ImportIntoSecureStoreAsync(
                    wireGuard.SecureConfigurationDirectory,
                    manifest.FullTunnelName,
                    manifest.FullConfigPath);

                var installed = new List<string>();
                try
                {
                    await InstallAndPrepareTunnelAsync(
                        wireGuard, serviceManager, homeDpapi, manifest.HomeTunnelName, manifest.RequestingUserSid);
                    installed.Add(manifest.HomeTunnelName);

                    // WireGuard's install command starts a tunnel immediately. Fully stop the Home-only
                    // instance before creating the Full instance so the two variants never compete for
                    // routes during setup.
                    await InstallAndPrepareTunnelAsync(
                        wireGuard, serviceManager, fullDpapi, manifest.FullTunnelName, manifest.RequestingUserSid);
                    installed.Add(manifest.FullTunnelName);
                }
                catch
                {
                    foreach (var tunnel in installed)
                        await wireGuard.UninstallTunnelAsync(tunnel, ignoreFailure: true);
                    throw;
                }
            }
            finally
            {
                // The manager service is needed only to let official WireGuard code encrypt plaintext
                // configs as LocalSystem. Restore the user's previous manager-service situation afterward.
                if (!managerExisted)
                {
                    try { await wireGuard.UninstallManagerAsync(); } catch { }
                }
                else if (!managerWasRunning)
                {
                    try { await TryStopServiceAsync("WireGuardManager"); } catch { }
                }
            }

            result = new AdminInstallResult { Success = true };
            await File.WriteAllTextAsync(manifest.ResultPath, JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            result = new AdminInstallResult { Success = false, Error = ex.Message };
            try
            {
                var manifest = JsonSerializer.Deserialize<AdminInstallManifest>(await File.ReadAllTextAsync(manifestPath));
                if (manifest is not null)
                    await File.WriteAllTextAsync(manifest.ResultPath, JsonSerializer.Serialize(result, JsonOptions));
            }
            catch
            {
                // Last resort: the parent process will report a missing result file.
            }
            return 1;
        }
    }

    private static async Task<string> ImportIntoSecureStoreAsync(string secureDirectory, string tunnelName, string sourceConfigPath)
    {
        var plainPath = Path.Combine(secureDirectory, tunnelName + ".conf");
        var encryptedPath = Path.Combine(secureDirectory, tunnelName + ".conf.dpapi");

        if (File.Exists(plainPath))
            File.Delete(plainPath);
        if (File.Exists(encryptedPath))
            File.Delete(encryptedPath);

        File.Copy(sourceConfigPath, plainPath, true);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(encryptedPath) && !File.Exists(plainPath))
                return encryptedPath;
            await Task.Delay(200);
        }

        throw new TimeoutException($"WireGuard Manager did not migrate '{tunnelName}.conf' into the protected DPAPI store in time.");
    }

    private static void DeleteSecureConfigIfPresent(string secureDirectory, string tunnelName)
    {
        foreach (var suffix in new[] { ".conf", ".conf.dpapi" })
        {
            var path = Path.Combine(secureDirectory, tunnelName + suffix);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A subsequent copy/install will surface a useful error if cleanup was required.
            }
        }
    }

    private static ServiceSnapshot SafeQuery(WindowsServiceManager serviceManager, string serviceName)
    {
        try { return serviceManager.Query(serviceName); }
        catch { return new ServiceSnapshot { Name = serviceName, State = WindowsServiceState.NotFound }; }
    }

    private static async Task InstallAndPrepareTunnelAsync(
        WireGuardCli wireGuard,
        WindowsServiceManager serviceManager,
        string protectedConfigPath,
        string tunnelName,
        string requestingUserSid)
    {
        await wireGuard.InstallTunnelAsync(protectedConfigPath);
        var serviceName = $"WireGuardTunnel${tunnelName}";
        await ConfigureManualStartAsync(serviceName);
        await GrantTunnelControlAsync(serviceName, requestingUserSid);
        await StopServiceAndWaitAsync(serviceManager, serviceName);
    }

    private static async Task StopServiceAndWaitAsync(WindowsServiceManager serviceManager, string serviceName)
    {
        var current = SafeQuery(serviceManager, serviceName);
        if (current.State is WindowsServiceState.NotFound or WindowsServiceState.Stopped)
            return;

        await ProcessRunner.RunAsync("sc.exe", new[] { "stop", serviceName });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = SafeQuery(serviceManager, serviceName).State;
            if (state is WindowsServiceState.Stopped or WindowsServiceState.NotFound)
                return;
            await Task.Delay(200);
        }

        throw new TimeoutException($"Service '{serviceName}' did not stop in time during setup.");
    }

    private static async Task ConfigureManualStartAsync(string serviceName)
    {
        var result = await ProcessRunner.RunAsync("sc.exe", new[] { "config", serviceName, "start=", "demand" });
        if (!result.Success)
            throw new InvalidOperationException($"Could not set service '{serviceName}' to manual start: {result.StandardError} {result.StandardOutput}".Trim());
    }

    private static async Task GrantTunnelControlAsync(string serviceName, string userSid)
    {
        var show = await ProcessRunner.RunAsync("sc.exe", new[] { "sdshow", serviceName });
        if (!show.Success)
            throw new InvalidOperationException($"Could not read service permissions for '{serviceName}'.");

        var sddl = show.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Contains("D:", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(sddl))
            throw new InvalidOperationException($"Could not parse service permissions for '{serviceName}'.");

        var ace = $"(A;;LCRPWPLO;;;{userSid})";
        if (sddl.Contains(ace, StringComparison.OrdinalIgnoreCase))
            return;

        var daclIndex = sddl.IndexOf("D:", StringComparison.Ordinal);
        var firstAceIndex = sddl.IndexOf('(', daclIndex + 2);
        var saclIndex = sddl.IndexOf("S:", daclIndex + 2, StringComparison.Ordinal);

        int insertAt;
        if (firstAceIndex >= 0 && (saclIndex < 0 || firstAceIndex < saclIndex))
            insertAt = firstAceIndex;
        else if (saclIndex >= 0)
            insertAt = saclIndex;
        else
            insertAt = sddl.Length;

        var updated = sddl.Insert(insertAt, ace);
        var set = await ProcessRunner.RunAsync("sc.exe", new[] { "sdset", serviceName, updated });
        if (!set.Success)
            throw new InvalidOperationException($"Could not grant start/stop permission on '{serviceName}': {set.StandardError} {set.StandardOutput}".Trim());
    }

    private static async Task WaitForServiceRunningAsync(WindowsServiceManager serviceManager, string serviceName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (serviceManager.Query(serviceName).IsRunning)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Service '{serviceName}' did not reach the running state in time.");
    }

    private static async Task TryStopServiceAsync(string serviceName)
    {
        await ProcessRunner.RunAsync("sc.exe", new[] { "stop", serviceName });
        await Task.Delay(200);
    }
}
