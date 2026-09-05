# Native runtime evidence (2026-09-05)

Reviewed upstream: WireGuard Windows **1.1**, commit
`4e6726c23ae9c5cb58e0c9910f3b7515621d133d`; WireGuardNT **1.1**.
Supported upstream architectures: x86, amd64, arm64. This package targets x64 only.

Primary sources (all source observations refer to the pinned commit):

- https://www.wireguard.com/embedding/
- https://git.zx2c4.com/wireguard-windows/tree/embeddable-dll-service/README.md?id=4e6726c23ae9c5cb58e0c9910f3b7515621d133d
- `embeddable-dll-service/main.go` and `csharp/TunnelDll/Service.cs` / `Driver.cs` / `Ringlogger.cs`
- `conf/name.go`, `conf/store.go`, `conf/dpapi/dpapi_windows.go`
- `tunnel/service.go`, `driver/dll_fromfile_windows.go`
- https://download.wireguard.com/wireguard-nt/wireguard-nt-1.1.zip

## ABI and service configuration

`WireGuardTunnelService(LPCWSTR)` is cdecl, returning Go/C `_Bool` (one byte).
The upstream README presents it as BOOL; HomeVPN explicitly marshals U1 to match
the actual exported Go bool. The call blocks for the service lifetime.
Services are `SERVICE_WIN32_OWN_PROCESS` (0x10), demand start (3), LocalSystem,
dependencies MULTI_SZ `Nsi\0TcpIp\0\0`, SID type UNRESTRICTED (1).
Demand start deliberately differs from upstream example automatic start: policy
must run after interactive login before any HomeVPN tunnel connects.

Upstream derives the SCM name from the configuration basename and **requires**
`WireGuardTunnel$` plus a 1–32-character tunnel name. Arbitrary
`HomeVPN.Tunnel.<guid>.Split` service names do not work with unmodified upstream.
HomeVPN encodes all 128 GUID bits as 26 base32 characters with `HVPN` prefix and
`S`/`F` suffix (31 characters total). No display-name/path coupling or truncation.

## No plaintext lifecycle

`tunnel/service.go` initializes ringlogger, then calls `conf.LoadFromPath` once,
before endpoint resolution, adapter creation, configuration and normal Running.
There is also an early Running report when SCM is locked at boot; Running alone
is therefore not proof of an adapter, route or handshake.

`conf/store.go` accepts `.conf.dpapi` and invokes native `CryptUnprotectData`.
`conf/dpapi` verifies the DPAPI description equals the tunnel basename, with no
optional entropy. Windows machine-scope DPAPI blobs are decryptable by SYSTEM.
HomeVPN uses `CryptProtectData`, flags UI_FORBIDDEN | LOCAL_MACHINE (5), with that
description. This is a better supported third option than A/B in the task:
**encrypted configuration files go directly to tunnel.dll; no plaintext file is
ever materialized**. This removes timing-based deletion and crash cleanup races.
The host additionally validates decrypted input before invoking upstream, clearing
byte buffers. Managed strings cannot be guaranteed erased; crash dumps are not
collected/uploaded by HomeVPN.

## DLL loading and logging

The service executable lives beside the two DLLs in `Program Files\HomeVPN\Runtime\x64`.
Upstream explicitly loads wireguard.dll using APPLICATION_DIR | SYSTEM32. Merely
calling AddDllDirectory for a nested runtime directory would not affect those
explicit flags. Keeping the host alongside DLLs preserves upstream unchanged.
HomeVPN loads absolute verified paths with DLL_LOAD_DIR | SYSTEM32 and removes
PATH/current-directory fallback via SetDefaultDllDirectories.

Upstream ringlogger writes `log.bin` beneath the configuration root. Its format
is magic 0x0badbabe, next index, 2048 slots of 8-byte nanosecond timestamp + 512
bytes UTF-8. Logs inherit the profile directory SYSTEM/Administrators DACL and
are not exported to the normal GUI. Parser errors never include input values;
hooks/unknown directives are rejected. Peer diagnostics marshal only handshake,
RX and TX from WireGuardGetConfiguration; the native buffer includes keys and
is zeroed without creating managed key objects. Ordinary GUI status uses SCM.

## Supply chain

`scripts/Get-Runtime.ps1` pins source commit and the upstream-pinned Go 1.26.2
and LLVM-MinGW 20260311 archives, verifying SHA256 before extraction. Go modules
are constrained by upstream go.mod/go.sum. The DLL build uses trimpath and
buildvcs=false; output hashes are checked against `native-hashes.json`.
Two builds in this checkout produced identical tunnel.dll hashes. CI is required
to reproduce these hashes as well. No `latest` release lookup occurs in builds.

WireGuardNT archive SHA256:
`dceb30a9bc4be48cce0f74160fc88a585a2c2627366e8f846fc6658f9038dace`

Runtime DLL hashes are maintained in the repository-root JSON. The installed
copy is under Program Files and checked before provisioning and loading.
Exact Windows COPYING and WireGuardNT LICENSE.txt are copied into the package;
the latter covers the prebuilt binary distribution. Native binary files and
toolchains are build outputs, never repository source assets.
