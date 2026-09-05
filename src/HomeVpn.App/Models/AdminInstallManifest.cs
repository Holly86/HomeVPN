namespace HomeVpn.Models;

public sealed class AdminInstallManifest
{
    public required string RequestingUserSid { get; init; }
    public required string HomeTunnelName { get; init; }
    public required string FullTunnelName { get; init; }
    public required string HomeConfigPath { get; init; }
    public required string FullConfigPath { get; init; }
    public required string ResultPath { get; init; }
    public List<string> OldTunnelNames { get; init; } = [];
}

public sealed class AdminInstallResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
}
