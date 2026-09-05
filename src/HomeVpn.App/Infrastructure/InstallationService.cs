namespace HomeVpn.Infrastructure;
public sealed class InstallationService
{
    public string InstallDirectory => NativeRuntime.InstallRoot;
    public string InstalledExecutablePath => Path.Combine(InstallDirectory, "HomeVPN.exe");
    public string CurrentExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException();
    public bool IsRunningInstalledCopy => string.Equals(CurrentExecutablePath, InstalledExecutablePath, StringComparison.OrdinalIgnoreCase);
    public bool EnsureInstalledAndRestartIfNeeded(string[] args) => false; // MSI owns installation; development runs stay in place.
}
