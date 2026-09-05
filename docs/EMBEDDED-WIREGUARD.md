# Embedded WireGuard backend

## Goal

Run HomeVPN on Windows 10/11 **without requiring the official WireGuard for Windows application to be installed separately**.

This is feasible using WireGuard's upstream-supported Windows embedding path.

## Upstream-supported approach

WireGuard explicitly recommends its **embeddable-dll-service** for Windows applications that want to embed WireGuard. It builds a platform-specific `tunnel.dll`. The current upstream documentation states that `tunnel.dll` also requires the platform-specific `wireguard.dll` from the WireGuardNT download server.

WireGuardNT is the lower-level API. Upstream recommends using `embeddable-dll-service` rather than integrating WireGuardNT directly unless the application specifically needs the lower-level adapter API.

References:

- https://www.wireguard.com/embedding/
- https://git.zx2c4.com/wireguard-windows/tree/embeddable-dll-service/README.md
- https://git.zx2c4.com/wireguard-nt/about/

## Resulting package

The user would install/download **HomeVPN only**, but the runtime directory would contain native components, for example:

```text
HomeVPN.exe
runtime/x64/tunnel.dll
runtime/x64/wireguard.dll
THIRD-PARTY-NOTICES.md
```

This can still be distributed as one installer/release package. It should not be advertised as literally one PE file unless the native DLLs are embedded as resources and extracted to a stable private runtime directory before service installation.

## Windows service model

The embedded backend still uses Windows services. A separate WireGuard application install is not required, but the initial setup remains an administrative operation.

For every derived tunnel HomeVPN creates a service similar to:

```text
Service Name: WireGuardTunnel$<TechnicalName>
Service Type: SERVICE_WIN32_OWN_PROCESS
Start Type: SERVICE_DEMAND_START
Dependencies: Nsi, TcpIp
SID Type: SERVICE_SID_TYPE_UNRESTRICTED
Executable: HomeVPN.exe --tunnel-service <protected-config-reference>
```

Upstream explicitly documents `SERVICE_SID_TYPE_UNRESTRICTED` as essential.

`HomeVPN.exe --tunnel-service ...` loads `tunnel.dll`, resolves `WireGuardTunnelService`, and runs the tunnel service. `tunnel.dll` in turn uses WireGuardNT through `wireguard.dll`.

The existing policy engine and service ACL model can therefore remain largely unchanged.

## Privileges

A one-time elevated setup is still needed to:

- create/remove Windows services;
- configure service SID type and dependencies;
- set service ACLs;
- allow WireGuardNT to install/load its kernel driver as needed.

After setup, HomeVPN can keep granting the interactive user only Query/Start/Stop/Interrogate rights on the managed tunnel services, preserving the current no-admin daily workflow.

## Configuration-secret handling

The current official-client backend benefits from `WireGuardManager` migrating `.conf` into its DPAPI-protected `.conf.dpapi` store.

The embedded backend cannot assume that manager exists, so HomeVPN must own secret-at-rest protection. Proposed design:

1. Import `.conf` in the interactive process.
2. Parse and validate it in memory.
3. Persist the canonical profile secret as a **machine-DPAPI encrypted envelope** under a HomeVPN system data directory with ACLs limited to SYSTEM and Administrators.
4. When a tunnel service starts, the SYSTEM-hosted HomeVPN service path decrypts the envelope.
5. Create a short-lived plaintext config with SYSTEM-only ACL if `tunnel.dll` requires a file path.
6. Invoke `WireGuardTunnelService` and remove the plaintext staging file immediately after the library has consumed it, subject to verification during implementation/testing.
7. Never place keys in `settings.json`, logs, crash reports, GitHub Actions artifacts or the repository.

An implementation review on Windows is required to verify the exact config-file lifetime expected by current `tunnel.dll` before deleting the plaintext file.

## Native binary supply chain

Do not commit an arbitrary downloaded `wireguard.dll` to the public repository.

Recommended release flow:

- pin an upstream WireGuard/WireGuardNT version;
- download `wireguard.dll` from the official WireGuardNT distribution during a trusted release build;
- build `tunnel.dll` from the matching upstream embeddable-dll-service source or use a controlled, reproducible build step;
- verify published SHA-256 hashes when upstream provides them / maintain pinned expected hashes in release automation;
- preserve upstream license/notices;
- Authenticode-sign HomeVPN's executable/installer and any project-built native DLL where practical.

## Licensing

The embeddable-dll-service example/library source is SPDX `MIT` in current upstream code. WireGuardNT's repository source is GPLv2, while upstream states that the prebuilt `wireguard.dll` binaries from its download server are distributed under more permissive terms contained in the downloaded archive.

Before shipping the embedded backend, the release process must preserve the exact license text/notices accompanying the pinned binary distribution. This document is an engineering note, not legal advice.

## Architecture impact

The public profile model already stores a `TunnelBackendKind`. Phase 1 uses `OfficialWireGuard`; Phase 2 can introduce an `EmbeddedWireGuard` provisioner while keeping:

- profile naming;
- Home-only/Full variants;
- desired/effective state;
- excluded-network policy;
- manual overrides;
- tray/UI behavior;
- Windows service start/stop/query control.

The main change is **provisioning and secret storage**, not the policy engine.

## Recommended implementation order

1. Stabilize and compile the current official-WireGuard backend.
2. Add unit tests around multi-profile policy and CIDR/network matching.
3. Introduce an `ITunnelProvisioner` abstraction for import/install/remove operations.
4. Implement embedded service-host command line (`--tunnel-service`).
5. Add native runtime acquisition/pinning for x64.
6. Implement protected secret envelope + SYSTEM service-side materialization.
7. Test install/uninstall/upgrade and MakeMeAdmin workflow on Windows 11.
8. Only then make embedded mode the default distribution.
