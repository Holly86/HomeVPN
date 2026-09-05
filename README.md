# HomeVPN for Windows

HomeVPN is a small native Windows VPN companion for FRITZ!Box-style WireGuard remote-access profiles. It adds network-aware auto-connect policy, Home-only vs. Full-Tunnel routing, excluded networks with session overrides, startup state restoration, multiple named VPN profiles, and a tray UI without requiring administrator rights for everyday connect/disconnect operations.

## Current scope

The first implementation uses an already installed **WireGuard for Windows** runtime. The code is intentionally structured around named profiles so the app is not tied to a hardcoded `Home` tunnel.

A second runtime backend is planned that uses WireGuard's official **embeddable-dll-service** and WireGuardNT so HomeVPN can operate without a separate WireGuard application installation. See [`docs/EMBEDDED-WIREGUARD.md`](docs/EMBEDDED-WIREGUARD.md).

## What it does

- Imports ordinary WireGuard `.conf` profiles from a FRITZ!Box or another compatible WireGuard endpoint.
- Lets the user assign every import a human-friendly profile name and a technical tunnel name.
- Supports a **Standard-VPN** plus additional named profiles such as `Mutter`, `Vater`, `Labor`, etc.
- Derives two local tunnel variants from each imported profile:
  - **Nur Heimnetz**: only configured target CIDRs are routed through the selected WireGuard tunnel (local internet breakout).
  - **Gesamter Verkehr**: default traffic is routed through the selected WireGuard tunnel.
- Persists desired ON/OFF state and routing mode **per profile**.
- Allows multiple Home-only profiles to be active in parallel. Full-Tunnel mode is deliberately exclusive to avoid competing `/0` routes and multiple Windows kill-switch policies.
- Keeps tunnel services on **manual start**. The app, not Windows service autostart, decides when a tunnel should run.
- Restores the user's saved profile states after Windows login.
- Automatically pauses VPN on user-defined excluded networks such as home or office LAN/WLAN.
- Allows an excluded network to be **temporarily overridden for the current network session** when the rule permits it.
- Supports profile-scoped exclusions. Example: being at your own home can suppress the `Home` profile while a second profile to a family member remains available.
- Detects connected Wi-Fi SSID and whether WLAN security is enabled. Unknown Wi-Fi can trigger a VPN recommendation; open Wi-Fi produces a stronger warning.
- Runs in the Windows notification area and uses a DPI-safe WPF UI.
- Stores **no WireGuard private key in HomeVPN settings**.

## Profile model

`HomeVPN` distinguishes:

- **Standard-VPN**: the preferred/default profile selected at startup.
- **Selected profile**: the profile currently shown and controlled by the main UI/tray.
- **Desired state per profile**: whether that profile should normally be connected.
- **Effective state per profile**: whether current network policy/routing constraints permit it to run.

Additional profiles can be selected and enabled independently. Home-only profiles may run in parallel when their remote networks are compatible. If any profile is using Full-Tunnel mode, HomeVPN treats it as exclusive because `/0` routing and WireGuard for Windows' full-tunnel kill-switch semantics should not compete.

## Requirements for the current backend

- Windows 10/11 x64
- Official **WireGuard for Windows** installed in the standard Program Files location
- One-time administrator rights when importing a VPN profile
- .NET is **not** required when using the self-contained release build

The planned embedded backend removes the separate WireGuard application requirement, but still needs a one-time elevated setup because Windows service/driver installation is privileged.

## First run

1. Download `HomeVPN.exe` from a release or GitHub Actions artifact.
2. Start it normally. The executable installs itself per-user to `%LOCALAPPDATA%\Programs\HomeVPN` and relaunches.
3. Click **VPN-Konfiguration importieren** and select a `.conf` exported by the FRITZ!Box/WireGuard endpoint.
4. Enter a friendly connection name and a unique technical tunnel name.
5. Verify the target-network CIDRs used for **Nur Heimnetz**.
6. Choose whether the new profile should become the **Standard-VPN**.
7. Complete the one-time elevated setup. On environments using temporary admin membership (for example a Make-Me-Admin workflow), activate that membership before confirming elevation.
8. Add or edit excluded networks in **Einstellungen**.

Further profiles can be imported later with **Weitere Verbindung importieren**.

After setup, HomeVPN only needs the service start/stop/query permissions granted during import; ordinary use does not require elevation.

## Excluded-network semantics

An exclusion is an **automatic-connect suppression rule**, not necessarily a permanent block.

Example:

- `Office` matches current SSID/subnet -> the affected profile stays off automatically.
- User clicks **Trotzdem verbinden** -> the exclusion is overridden for this network session.
- The network changes or HomeVPN restarts -> the override disappears and `Office` is excluded again.

When a rule contains both a network-name/SSID pattern and a subnet, **both must match**. This reduces false positives on common RFC1918 ranges.

Rules with no profile scope apply globally. Automatically created `Zuhause · <Profil>` rules are scoped to the imported profile only.

## Full tunnel note

Full Tunnel uses `0.0.0.0/0` and adds `::/0` when IPv6 is present in the imported profile. WireGuard for Windows applies its `/0` full-tunnel / kill-switch semantics. Whether internet access through the tunnel works also depends on the remote WireGuard/FRITZ!Box configuration.

## Secrets and the public repository

Never commit a real VPN configuration. The repository ignores:

```text
*.conf
*.conf.dpapi
*.key
*.pem
*.pfx
*.p12
secrets/
vpn-config/
```

The runtime import is deliberately local; source `.conf` files are not added to the project or repository.

## Build

```powershell
dotnet restore HomeVPN.sln
dotnet build HomeVPN.sln -c Release
dotnet publish src/HomeVpn.App/HomeVpn.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

The included GitHub Actions workflow produces a self-contained `HomeVPN.exe` on `windows-latest`.

## Design

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) – state model, multiple profiles, service ACL model and network policy.
- [`docs/EMBEDDED-WIREGUARD.md`](docs/EMBEDDED-WIREGUARD.md) – plan for operating without a separate WireGuard for Windows installation.

## License

MIT. HomeVPN is an independent application. The current backend controls an existing WireGuard for Windows installation; future bundled WireGuard components retain their own upstream licenses and notices.
