# HomeVPN for Windows

HomeVPN is a native WPF application with an embedded official WireGuard runtime. A separate WireGuard application is not required. Windows 10/11 x64 is supported; the installer includes .NET.

## Install and import

Build or download HomeVPN-Setup-win-x64.exe and run the per-machine installer. HomeVPN installs to Program Files\HomeVPN. You can install without a VPN configuration and import one later.

Start HomeVPN normally, select a local WireGuard .conf, choose a display name and verify the remote target CIDRs. Choose a primary profile, optionally exclude the current network, and enable login autostart. Import elevates once to provision two demand-start services and perform a split-only test. An unreachable peer produces a warning with Retry; it does not silently replace the default route.

After setup the importing account can query, start, stop and interrogate its own services without elevation. Service configuration and encrypted keys remain administrator protected. Temporary-admin workflows require same-account elevation; alternate administrator credentials are deliberately rejected.

## Connection policy

Each immutable GUID identifies a profile independently of its editable name. Desired on/off and routing mode persist per profile. Effective state reflects SCM state and policy errors are shown separately. Primary and selected profiles are distinct.

- **Nur Heimnetz:** only verified target CIDRs; local internet stays local. Optional [split DNS](docs/SPLIT-DNS.md) sends configured home domains to a remote home DNS server; other names keep local DNS.
- **Gesamter Verkehr:** IPv4 default route, plus IPv6 default when the imported interface has IPv6. Remote peer forwarding must support this.
- Non-overlapping split profiles can run together. Full tunnel is exclusive. Overlapping CIDRs block the losing profile without clearing its desired state. All losing services stop before any winning service starts.
- Exclusions suppress automatic connection. “Trotzdem verbinden” overrides a permitted rule only for the current network fingerprint; address/SSID changes, network loss and app restart clear it.
- Open WLAN produces a warning and unknown WLAN a recommendation; neither silently forces full tunnel.
- Active foreign WireGuard adapters conservatively block HomeVPN connections. HomeVPN never automatically stops or deletes them.

## Protected configuration

The strict parser supports one Interface and one Peer, rejects hooks and unknown directives, and deterministically renders split/full variants. Windows machine-scope DPAPI encrypts each variant under ProgramData\HomeVPN\Profiles\GUID. Only SYSTEM and Administrators can access that tree. The official tunnel.dll reads .conf.dpapi directly: no plaintext staging file exists. LocalAppData settings contain metadata only.

Legacy settings remain readable but require reimport. Their previous WireGuard services are never adopted or removed on ambiguous ownership. The old per-user executable is not automatically deleted. See [architecture](docs/ARCHITECTURE.md), [upstream evidence](docs/UPSTREAM-PINS.md), [security review](SECURITY.md) and [validation record](docs/VALIDATION.md).

## Build

On Windows with Git and .NET 8 SDK:

~~~powershell
./scripts/Build-Installer.ps1
~~~

The script downloads pinned official toolchains and WireGuardNT, verifies archive and output hashes, builds the upstream tunnel DLL, restores/builds/tests managed projects, publishes self-contained apps, and creates a WiX 5 MSI + Burn bootstrapper. Outputs are under artifacts/installer and artifacts/HomeVPN. Native archives and real configurations must never be committed.

For managed checks only: dotnet test HomeVPN.sln -c Release. The test-only VisualHarness is excluded from the installer; its synthetic scenarios never control services. Scaling in this harness is layout simulation, not proof of actual Windows DPI changes.

## Maintenance

Setup offers repair/removal. The profile retention checkbox defaults to keeping encrypted profiles for reinstall. Unchecked removes HomeVPN-owned protected configuration only. For unattended removal use KEEP_PROFILES=0 explicitly. Services are recreated as demand start during reinstall; no connection begins before user policy evaluation.

Current validation and remaining manual acceptance work are documented in docs/VALIDATION.md. Local development packages are unsigned; production signing requires the project owner's certificate and is not fabricated.
