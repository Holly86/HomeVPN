using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using HomeVpn.Models;
using Microsoft.Win32;

namespace HomeVpn.Infrastructure;

/// <summary>Own NRPT rules only. A SYSTEM companion follows the tunnel host, including parent crashes.</summary>
public static class SplitDnsRuntime
{
    private sealed record Record(Guid Tag, SplitDnsSettings Settings);
    private static string RecordPath(Guid id) => Path.Combine(MachineSecrets.ProfileDirectory(id), "dns.json");
    private static Record? Read(Guid id)
    {
        MachineSecrets.VerifyDirectory(MachineSecrets.ProfileDirectory(id));
        var path = RecordPath(id);
        MachineSecrets.RejectReparsePoints(path);
        return File.Exists(path) ? JsonSerializer.Deserialize<Record>(File.ReadAllText(path)) : null;
    }

    public static async Task ConfigureAsync(VpnProfile profile, SplitDnsSettings settings)
    {
        if (new WindowsServiceManager().Query(profile.HomeServiceName).State != WindowsServiceState.Stopped
            || new WindowsServiceManager().Query(profile.FullServiceName).State != WindowsServiceState.Stopped)
            throw new InvalidOperationException("Bitte die Verbindung vor der DNS-Änderung trennen.");
        using var gate = await LockAsync(profile.Id.ToString("N"));
        var previous = Read(profile.Id);
        if (previous is not null) await ApplyAsync(profile.Id, previous, false);
        var record = new Record(previous?.Tag ?? Guid.NewGuid(), SplitDns.Normalize(settings, profile.HomeCidrs));
        MachineSecrets.WriteAtomic(RecordPath(profile.Id), JsonSerializer.SerializeToUtf8Bytes(record));
        profile.SplitDns = record.Settings;
        MachineSecrets.WriteAtomic(Path.Combine(MachineSecrets.ProfileDirectory(profile.Id), "profile.json"), JsonSerializer.SerializeToUtf8Bytes(profile));
        Status(profile.Id, "inactive");
    }

    public static Process? StartCompanion(Guid id)
    {
        if (Read(id)?.Settings.Enabled != true) return null;
        var start = new ProcessStartInfo(NativeRuntime.HostPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        start.ArgumentList.Add("--dns-watch"); start.ArgumentList.Add(id.ToString("N")); start.ArgumentList.Add(Environment.ProcessId.ToString());
        return Process.Start(start) ?? throw new InvalidOperationException("Split-DNS konnte nicht gestartet werden.");
    }

    public static async Task WatchAsync(Guid id, int parentId)
    {
        using var parent = Process.GetProcessById(parentId);
        if (!string.Equals(parent.MainModule?.FileName, NativeRuntime.HostPath, StringComparison.OrdinalIgnoreCase)
            || !EmbeddedProvisioner.IsOwned(id, RoutingMode.HomeOnly)) throw new UnauthorizedAccessException();
        // EOF arrives both on graceful tunnel completion and if its host crashes. No GUI lifetime dependency.
        var completed = Task.Run(() => Console.In.ReadLine());
        using var gate = await LockAsync(id.ToString("N"));
        var record = Read(id);
        if (record is null || !record.Settings.Enabled) return;
        var profile = JsonSerializer.Deserialize<VpnProfile>(File.ReadAllText(Path.Combine(MachineSecrets.ProfileDirectory(id), "profile.json")))!;
        record = record with { Settings = SplitDns.Normalize(record.Settings, profile.HomeCidrs) };
        bool applied = false;
        try
        {
            await ApplyAsync(id, record, false); // recover a stale own rule before using the new tunnel session
            while (!completed.IsCompleted && !parent.HasExited)
            {
                var services = new WindowsServiceManager();
                var adapter = NetworkInterface.GetAllNetworkInterfaces().Any(x => x.Name == TunnelIdentity.Name(id, RoutingMode.HomeOnly) && x.OperationalStatus == OperationalStatus.Up);
                bool desired = SplitDns.ShouldApply(services.Query(profile.HomeServiceName).IsRunning, adapter, services.Query(profile.FullServiceName).IsRunning);
                if (desired != applied) { await ApplyAsync(id, record, desired); applied = desired; }
                Status(id, applied ? "applied" : "waiting");
                await Task.WhenAny(completed, Task.Delay(1000));
            }
        }
        catch { Status(id, "error"); throw; }
        finally
        {
            await ApplyAsync(id, record, false);
            Status(id, "inactive");
        }
    }

    public static async Task RemoveAsync(Guid id)
    {
        if (!File.Exists(RecordPath(id))) return;
        using var gate = await LockAsync(id.ToString("N"));
        var record = Read(id);
        if (record is not null) await ApplyAsync(id, record, false);
        Status(id, "inactive");
    }

    private static async Task<FileStream> LockAsync(string name)
    {
        MachineSecrets.VerifyDirectory(MachineSecrets.Root);
        var path = Path.Combine(MachineSecrets.Root, "dns-" + name + ".lock");
        MachineSecrets.RejectReparsePoints(path);
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 150) { await Task.Delay(200); }
        }
    }

    private static async Task ApplyAsync(Guid id, Record record, bool enabled)
    {
        // All profiles serialize NRPT conflict checks and writes. The lock is in the protected machine store.
        using var gate = await LockAsync("nrpt");
        var payload = JsonSerializer.Serialize(new { DisplayName = "HomeVPN.SplitDNS." + id.ToString("N"),
            Comment = "HomeVPN owned " + record.Tag.ToString("N"), Enabled = enabled,
            Namespaces = SplitDns.Namespaces(record.Settings), record.Settings.Server });
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows-DNS-Verwaltung konnte nicht gestartet werden.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(payload); process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Task.WhenAll(output, errors);
            if (process.ExitCode != 0) throw new InvalidOperationException("Split-DNS konnte nicht angewendet werden. Bestehende DNS-Richtlinien und den Heim-DNS prüfen.");
        }
        catch
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            throw;
        }
    }

    private static void Status(Guid id, string state)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(@"Software\HomeVPN\DnsStatus\" + id.ToString("N"));
        key.SetValue("State", state); key.SetValue("Updated", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), RegistryValueKind.QWord);
    }

    public static string DisplayStatus(Guid id)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"Software\HomeVPN\DnsStatus\" + id.ToString("N"));
            if (key?.GetValue("Updated") is long updated && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - updated < 10
                && key.GetValue("State") is string state && state == "applied") return "Split-DNS aktiv";
        }
        catch { }
        return "Split-DNS noch nicht bestätigt – DNS-Einstellungen prüfen";
    }

    // Fixed script + JSON stdin: no caller text is executable PowerShell. Never replace adapter DNS or foreign rules.
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        Import-Module (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\Modules\DnsClient\DnsClient.psd1')
        $p = [Console]::In.ReadToEnd() | ConvertFrom-Json
        $all = @(Get-DnsClientNrptRule)
        $owned = @($all | Where-Object { $_.DisplayName -eq $p.DisplayName -and $_.Comment -eq $p.Comment })
        foreach ($rule in $owned) { Remove-DnsClientNrptRule -Name $rule.Name -Force }
        if ($p.Enabled) {
            foreach ($rule in $all) {
                if ($rule.DisplayName -eq $p.DisplayName -and $rule.Comment -eq $p.Comment) { continue }
                foreach ($ns in $rule.Namespace) {
                    foreach ($target in $p.Namespaces) {
                        $a = $ns.TrimStart('.'); $b = $target.TrimStart('.')
                        if ($ns -eq '.' -or $a -eq $b -or $a.EndsWith('.' + $b, [StringComparison]::OrdinalIgnoreCase) -or $b.EndsWith('.' + $a, [StringComparison]::OrdinalIgnoreCase)) {
                            throw 'Existing DNS namespace policy conflicts with HomeVPN.'
                        }
                    }
                }
            }
            Add-DnsClientNrptRule -Namespace ([string[]]$p.Namespaces) -NameServers $p.Server -DisplayName $p.DisplayName -Comment $p.Comment | Out-Null
            $effective = @(Get-DnsClientNrptPolicy -Effective)
            foreach ($target in $p.Namespaces) {
                if (-not @($effective | Where-Object { @($_.Namespace) -contains $target -and @($_.NameServers) -contains $p.Server }).Count) {
                    Get-DnsClientNrptRule | Where-Object { $_.DisplayName -eq $p.DisplayName -and $_.Comment -eq $p.Comment } | ForEach-Object { Remove-DnsClientNrptRule -Name $_.Name -Force }
                    throw 'HomeVPN DNS rule is not effective, possibly due to Group Policy.'
                }
            }
        }
        Clear-DnsClientCache
        """;
}
