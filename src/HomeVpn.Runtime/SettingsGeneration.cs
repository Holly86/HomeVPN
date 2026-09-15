using Microsoft.Win32;

namespace HomeVpn.Infrastructure;

// Public, non-secret machine marker. Purging never follows paths from user profiles as SYSTEM.
// Each GUI resets its own metadata under that user's token after a machine-wide purge.
public static class SettingsGeneration
{
    private const string Key = @"Software\HomeVPN";
    public static string? Read()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(Key);
        return key?.GetValue("SettingsGeneration") as string;
    }

    public static void Reset()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Key, true);
        key.SetValue("SettingsGeneration", Guid.NewGuid().ToString("N"));
    }
}
