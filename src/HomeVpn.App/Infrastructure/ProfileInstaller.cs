using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public sealed class ProfileInstaller
{
    private readonly SettingsStore _settingsStore;
    private readonly InstallationService _installationService;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ProfileInstaller(SettingsStore settingsStore, InstallationService installationService)
    {
        _settingsStore = settingsStore;
        _installationService = installationService;
    }

    public async Task<VpnProfile> InstallAsync(
        WireGuardConfig config,
        string displayName,
        string requestedTunnelName,
        IReadOnlyList<string> homeCidrs,
        VpnProfile? oldProfile,
        CancellationToken cancellationToken = default)
    {
        const string fullSuffix = "-Full";
        var baseTunnelName = WireGuardConfig.SanitizeTunnelName(requestedTunnelName);
        var maxBaseLength = 32 - fullSuffix.Length;
        if (baseTunnelName.Length > maxBaseLength)
            baseTunnelName = baseTunnelName[..maxBaseLength].TrimEnd('.');
        if (string.IsNullOrWhiteSpace(baseTunnelName))
            baseTunnelName = "Home";
        var fullTunnelName = baseTunnelName + fullSuffix;

        var stagingDirectory = Path.Combine(_settingsStore.DataDirectory, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);

        var homeConfigPath = Path.Combine(stagingDirectory, baseTunnelName + ".conf");
        var fullConfigPath = Path.Combine(stagingDirectory, fullTunnelName + ".conf");
        var manifestPath = Path.Combine(stagingDirectory, "install.json");
        var resultPath = Path.Combine(stagingDirectory, "result.json");

        try
        {
            await File.WriteAllTextAsync(homeConfigPath, config.CreateHomeOnlyVariant(homeCidrs), cancellationToken);
            await File.WriteAllTextAsync(fullConfigPath, config.CreateFullTunnelVariant(), cancellationToken);

            using var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User?.Value ?? throw new InvalidOperationException("Could not determine the current user SID.");

            var manifest = new AdminInstallManifest
            {
                RequestingUserSid = userSid,
                HomeTunnelName = baseTunnelName,
                FullTunnelName = fullTunnelName,
                HomeConfigPath = homeConfigPath,
                FullConfigPath = fullConfigPath,
                ResultPath = resultPath,
                OldTunnelNames = oldProfile is null
                    ? []
                    : new[] { oldProfile.HomeTunnelName, oldProfile.FullTunnelName }.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions), cancellationToken);

            var executable = File.Exists(_installationService.InstalledExecutablePath)
                ? _installationService.InstalledExecutablePath
                : _installationService.CurrentExecutablePath;

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--admin-install \"{manifestPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };

            try
            {
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch the elevated installer.");
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Administrator elevation was cancelled. If this device uses MakeMeAdmin, enable temporary administrator rights and try again.", ex);
            }

            if (!File.Exists(resultPath))
                throw new InvalidOperationException("The elevated installation did not return a result. If you use MakeMeAdmin, enable temporary administrator rights and try again.");

            var result = JsonSerializer.Deserialize<AdminInstallResult>(await File.ReadAllTextAsync(resultPath, cancellationToken))
                         ?? throw new InvalidDataException("Invalid installer result.");
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "WireGuard profile installation failed.");

            return new VpnProfile
            {
                Id = oldProfile?.Id ?? Guid.NewGuid(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? baseTunnelName : displayName.Trim(),
                HomeTunnelName = baseTunnelName,
                FullTunnelName = fullTunnelName,
                HomeCidrs = homeCidrs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ImportedAt = DateTimeOffset.UtcNow,
                DesiredVpnEnabled = oldProfile?.DesiredVpnEnabled ?? false,
                RoutingMode = oldProfile?.RoutingMode ?? RoutingMode.HomeOnly,
                Backend = TunnelBackendKind.OfficialWireGuard
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, true);
            }
            catch
            {
                // Secrets are staged only under the user's LocalAppData and normally removed immediately.
            }
        }
    }
}
