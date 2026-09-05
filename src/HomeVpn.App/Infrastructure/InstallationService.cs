using System.Diagnostics;

namespace HomeVpn.Infrastructure;

public sealed class InstallationService
{
    public string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "HomeVPN");

    public string InstalledExecutablePath => Path.Combine(InstallDirectory, "HomeVPN.exe");

    public string CurrentExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");

    public bool IsRunningInstalledCopy => PathsEqual(CurrentExecutablePath, InstalledExecutablePath);

    public bool EnsureInstalledAndRestartIfNeeded(string[] args)
    {
        if (args.Contains("--portable", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--admin-install", StringComparer.OrdinalIgnoreCase) ||
            IsRunningInstalledCopy)
            return false;

        Directory.CreateDirectory(InstallDirectory);
        File.Copy(CurrentExecutablePath, InstalledExecutablePath, true);

        var psi = new ProcessStartInfo
        {
            FileName = InstalledExecutablePath,
            UseShellExecute = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        Process.Start(psi);
        return true;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd('\\'), Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
