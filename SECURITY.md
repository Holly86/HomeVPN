# Security

## VPN configuration files

WireGuard `.conf`, `.conf.dpapi`, key and certificate files are ignored by `.gitignore` and must never be committed.

HomeVPN does not persist imported plaintext WireGuard configurations in its normal application settings. Persistent settings contain profile IDs/names, service names, routing CIDRs, desired state, routing mode and network-policy rules only. They do not contain WireGuard private keys.

### Current official-WireGuard backend

During import HomeVPN creates short-lived derived `.conf` files under `%LOCALAPPDATA%\HomeVPN\staging`, copies them into the official WireGuard configuration store, and lets the official `WireGuardManager` service encrypt them as LocalSystem into `.conf.dpapi` files. The tunnel services are installed from those protected files. HomeVPN deletes its staging directory in a `finally` block.

### Future embedded backend

The embedded backend must not regress secret-at-rest protection. The planned design uses a machine-DPAPI-protected profile envelope owned by SYSTEM/Administrators and only short-lived SYSTEM-only plaintext materialization if required by upstream `tunnel.dll`. See `docs/EMBEDDED-WIREGUARD.md`.

## Elevated operations

Elevation is limited to profile installation/replacement/removal and future embedded-runtime provisioning. Day-to-day connect/disconnect operations use narrowly scoped ACLs on the managed Windows services. The app does not store administrator credentials.

## Multiple profiles

Every imported profile gets its own service pair and ACL. Home-only profiles may run in parallel. Full-Tunnel is exclusive to avoid conflicting `/0` routes and multiple simultaneous kill-switch policies.

## Reporting issues

Do not attach real WireGuard configuration files or private keys to public issues. Redact endpoints, keys and identifying SSIDs/subnets where appropriate.
