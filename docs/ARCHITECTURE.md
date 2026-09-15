# Architecture

## Boundaries

HomeVpn.App owns WPF/tray, normal-user metadata, network detection and policy orchestration. HomeVpn.Runtime owns strict config parsing, identity, protected storage, provisioning, SCM control and diagnostics. HomeVpn.TunnelService is a dedicated LocalSystem process alongside Runtime/x64/tunnel.dll and wireguard.dll.

ITunnelController exposes query/start/stop. ITunnelProvisioner handles elevated create/remove. PolicyPlanner is pure; PolicyTransition applies all stops before starts and blocks starts on any failed stop. The background engine serializes commands, suspends during provisioning, and publishes desired/effective/reason snapshots to the UI dispatcher.

## Import transaction

GUI parses the selected local file in memory, collects name/remote CIDRs, suspends policy, then creates a random first-instance named pipe with an explicit account-SID DACL. It launches the installed executable via runas with only the pipe name. The server verifies the elevated client PID before sending any configuration; the helper verifies the server owner against its TokenUser SID. Identification-level client impersonation prevents a server from impersonating the elevated client for privileged operations. Frames are length bounded and transient byte arrays are zeroed.

The helper verifies pinned native hashes, validates input again, creates an immutable GUID, encrypts two variants, creates manual services, sets minimal control ACLs and records ownership in the protected directory. Failed provisioning rolls back only resources created by that attempt. The split-only diagnostic returns separate runtime/service/adapter/route/handshake results. The GUI persists returned metadata and keeps new desired state off until explicit activation.

.NET 8 CurrentUserOnly compares TokenOwner, which changes under UAC; HomeVPN uses explicit TokenUser ownership instead. The real initial pipe failure and correction are recorded in VALIDATION.md.

## Identity and authority

Upstream requires WireGuardTunnel$ plus the configuration basename. HomeVPN uses HVPN + full 128-bit GUID base32 + S/F (31 characters). Service executable paths are quoted and contain only a GUID and mode. No arbitrary path or display name is accepted by the service host. The host verifies LocalSystem, its installed location, protected owner record and matching SCM BinaryPath.

Machine ownership metadata is authoritative for service deletion/restoration. User-editable JSON does not grant provisioning authority. Old backend profiles are display/migration-only. Separate foreign WireGuard installations are not dependencies and are not modified by policy or maintenance.

## Secrets and lifecycle

See UPSTREAM-PINS.md for the exact native lifecycle. The persistent .conf.dpapi is passed directly to upstream; there is no plaintext-file lifetime to race. SYSTEM/Administrators-only parent directories protect atomic temporary writes and upstream ringlogger. The GUI never reads encrypted files or ringlogger; it receives sanitized diagnostic scalars through the setup pipe.

## Optional split DNS

Split DNS is configured per profile through a bounded elevated setup request. Protected DNS metadata is independent of key material. A SYSTEM companion tracks the split host lifetime and adapter state, applies only GUID/tag-owned NRPT namespace rules, and removes them on disconnect. Physical adapter DNS is unchanged; full mode retains imported DNS. See SPLIT-DNS.md for policy arbitration, lifetime, ownership and deferred live checks.

## Installer

WiX 5 MSI performs per-machine x64 installation with a Burn bootstrapper. The service host and both DLLs are colocated because upstream loads wireguard.dll from APPLICATION_DIR | SYSTEM32. Dynamic profile services are maintained through a bundled deferred, non-impersonated maintenance executable. Upgrade removes only verified owned services before replacing files and restores demand-start services afterwards; rollback schedules restoration. Final uninstall can preserve encrypted profiles or purge them. Autostart cleanup is separate from upgrade service removal.

## Limits

One-peer configurations only; x64 only. Same-account UAC is required. GUI “connected” means service Running, not a continuously verified peer handshake. Handshake diagnostics require elevation and run during setup/retry. Dynamic user-profile discovery and offline-user autostart/metadata cleanup need explicit validation; see the acceptance matrix.
