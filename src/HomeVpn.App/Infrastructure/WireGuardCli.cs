namespace HomeVpn.Infrastructure;

public sealed class WireGuardCli
{
    public string ExecutablePath { get; }
    public string SecureConfigurationDirectory => Path.Combine(
        Path.GetDirectoryName(ExecutablePath) ?? throw new InvalidOperationException("WireGuard installation path unavailable."),
        "Data",
        "Configurations");

    public WireGuardCli()
    {
        ExecutablePath = FindExecutable() ?? throw new FileNotFoundException(
            "WireGuard for Windows was not found. Install WireGuard first.");
    }

    public async Task InstallTunnelAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            ExecutablePath,
            new[] { "/installtunnelservice", configPath },
            cancellationToken: cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"WireGuard could not install the tunnel. {result.StandardError} {result.StandardOutput}".Trim());
    }

    public async Task UninstallTunnelAsync(string tunnelName, bool ignoreFailure = true, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            ExecutablePath,
            new[] { "/uninstalltunnelservice", tunnelName },
            cancellationToken: cancellationToken);

        if (!result.Success && !ignoreFailure)
            throw new InvalidOperationException($"WireGuard could not uninstall tunnel '{tunnelName}'. {result.StandardError} {result.StandardOutput}".Trim());
    }

    public async Task InstallManagerAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            ExecutablePath,
            new[] { "/installmanagerservice" },
            cancellationToken: cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException($"WireGuard Manager could not be installed. {result.StandardError} {result.StandardOutput}".Trim());
    }

    public async Task UninstallManagerAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            ExecutablePath,
            new[] { "/uninstallmanagerservice" },
            cancellationToken: cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException($"WireGuard Manager could not be removed. {result.StandardError} {result.StandardOutput}".Trim());
    }

    public static string? FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
