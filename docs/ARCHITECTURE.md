# Architecture

HomeVPN is a small Windows policy/controller layer around WireGuard tunnel runtimes.
The current runtime backend controls the official WireGuard for Windows tunnel services. A future backend can use the official embeddable WireGuard tunnel library without changing the user-facing profile/policy model.

## Multiple profile model

A VPN connection is never hardcoded as `Home`.

Each imported profile contains:

- stable `Id`
- user-facing `DisplayName`
- technical `HomeTunnelName` and `FullTunnelName`
- Home-only target CIDRs
- per-profile desired ON/OFF state
- per-profile routing mode
- runtime backend identifier

Global settings contain:

- `PrimaryProfileId` – the Standard-VPN
- `SelectedProfileId` – the profile currently controlled by the main UI/tray
- list of profiles
- global and profile-scoped excluded-network rules

The first imported profile becomes the Standard-VPN by default. Any later import can explicitly become the new Standard-VPN.

## Runtime state

For every profile HomeVPN keeps separate concepts:

- **Desired state**: whether the user wants this profile enabled in general.
- **Effective state**: whether current network/routing policy permits this profile to run.
- **Session override**: an in-memory override for one excluded network session. It is never persisted.

Policy precedence for each profile:

1. User explicitly disabled this profile -> off.
2. No usable physical network -> off.
3. Matching excluded network + no current-session override -> off.
4. Matching excluded network + allowed manual override -> on after explicit user action.
5. Otherwise apply desired state.

A network change invalidates all session overrides.

## Parallel tunnel rules

- Multiple **Home-only** profiles may run in parallel.
- A **Full-Tunnel** profile is exclusive.
- When more than one desired profile requests Full-Tunnel, the currently selected profile wins, then the Standard-VPN, then profile order.
- Other desired profiles receive a `RouteConflict` effective state until the Full-Tunnel profile is stopped or changed back to Home-only.

This avoids competing `0.0.0.0/0` / `::/0` routes and multiple simultaneous WireGuard-for-Windows full-tunnel kill-switch policies.

Overlapping Home-only target networks remain an operational consideration; a future route-conflict analyzer can warn before enabling two overlapping profiles.

## Two service variants per imported profile

An imported single-peer WireGuard configuration is used to derive two local configurations:

- `<name>`: Home-only split tunnel. `AllowedIPs` is replaced with the configured target CIDRs.
- `<name>-Full`: Full tunnel. `AllowedIPs` is replaced with `0.0.0.0/0` and, when the imported profile contains IPv6, `::/0`.

Both variants retain the same interface key, peer public key, endpoint, DNS and other imported settings.

### Current backend: official WireGuard for Windows

The temporary plaintext variants are copied into WireGuard for Windows' official configuration store while the `WireGuardManager` service is running. The official manager encrypts them as LocalSystem into `.conf.dpapi` files and deletes the plaintext copies. HomeVPN then installs the tunnel services from those protected files and removes its own staging directory. HomeVPN stores only metadata in `%LOCALAPPDATA%\HomeVPN\settings.json`.

### Planned backend: embedded WireGuard

The upstream-recommended `embeddable-dll-service` lets HomeVPN host WireGuard tunnel services itself using `tunnel.dll` plus `wireguard.dll`. This removes the dependency on the separately installed WireGuard Windows client while retaining the Windows-service model. See `EMBEDDED-WIREGUARD.md`.

## Privilege model

Normal operation runs without elevation.

One elevated import/setup step for the current backend:

1. Installs/replaces the two WireGuard tunnel services for that profile.
2. Sets both services to `demand` start.
3. Grants the importing Windows user's SID only the service rights needed to query/start/stop/interrogate those two services (`LCRPWPLO`).
4. Stops both services so the policy engine determines the effective state.

Each additional imported profile receives its own scoped service ACLs.

The app does not grant `SERVICE_CHANGE_CONFIG` and does not use WireGuard PreUp/PostUp scripts.

## Network detection

- Physical LAN/WLAN adapters are discovered using `System.Net.NetworkInformation`.
- Common virtual/tunnel adapters are excluded from policy matching.
- Connected Wi-Fi SSID and the Wi-Fi security-enabled flag are read via the native Windows WLAN API (`wlanapi.dll`).
- Exclusion rules can match network name/SSID, local subnet CIDR, or both. If both are set, both must match.
- `*` and `?` wildcards are accepted in network-name patterns.
- Rules can be global or scoped to specific profile IDs.

Unknown Wi-Fi while the selected VPN is manually off produces a recommendation. An unencrypted Wi-Fi produces a stronger warning.

## Startup

The published single-file executable copies itself to `%LOCALAPPDATA%\Programs\HomeVPN\HomeVPN.exe` on first launch and relaunches from there. Autostart is per-user via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Tunnel services themselves remain manual-start services. This prevents Windows from reconnecting a tunnel before HomeVPN has evaluated the current network and all profile policies.
