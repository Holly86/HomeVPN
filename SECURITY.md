# Security model and review

Never commit real WireGuard configurations, private/preshared keys, DPAPI blobs, crash dumps or raw runtime logs. Ignore rules are defense in depth; release review scans the actual tracked files too.

## Privilege boundaries

Normal GUI settings contain profile IDs/names, target CIDRs, desired state and network rules. No keys, endpoint credentials or plaintext configuration are stored there. Elevated setup receives a bounded JSON frame over a randomly named first-instance pipe whose protected DACL and owner are the importing account SID. GUI checks the exact spawned client PID before transmitting secrets. The helper checks pipe ownership using TokenUser rather than .NET 8's elevation-sensitive TokenOwner; it connects with identification-only impersonation. Alternate-account UAC is rejected.

The privileged helper accepts import/test/remove and validated per-profile split-DNS configuration operations only. It derives all paths and service names from immutable GUIDs and validates input again. It does not execute caller-selected binaries, scripts or configuration hooks. Serialized objects are explicit sealed records/classes with no type-name polymorphism.

## Storage

DPAPI LOCAL_MACHINE + UI_FORBIDDEN, with the exact upstream tunnel-name description, protects each split/full configuration. The official DLL decrypts it directly. ProgramData/HomeVPN and profile directories have inheritance disabled and SYSTEM/Administrators full control only. Existing directories with unexpected owner or allow ACEs are rejected. Atomic writes use an unpredictable CreateNew sibling, exclusive sharing and flushed replacement. Reparse points are rejected along protected paths; user-controlled display names never enter paths.

No plaintext configuration is written to temporary storage. Managed parser strings and native WireGuard memory necessarily contain keys in memory; .NET cannot promise string erasure. Byte buffers and native diagnostic buffers are cleared. The application does not collect or upload crash dumps. Source .conf files remain where the user selected them.

A machine-wide purge writes a non-secret generation marker in HKLM. On its next start each GUI discards and rewrites its own stale metadata under the normal user token. This deliberately avoids privileged traversal of user-controlled profile directories; old metadata can remain on disk until that user next starts HomeVPN.

## Services and DLLs

Services run as LocalSystem, OWN_PROCESS, manual start, dependencies Nsi/TcpIp and unrestricted SID. The importing user's DACL mask is 0xB4: QUERY_STATUS, START, STOP, INTERROGATE. No CHANGE_CONFIG, DELETE, WRITE_DAC or WRITE_OWNER is granted. SYSTEM/Administrators retain full control. Display-name changes do not change service identity.

Service paths are quoted absolute Program Files paths and receive only a GUID/mode. The host verifies LocalSystem, its fixed location, protected ownership and exact SCM binary path before loading. Both DLL hashes are checked against the protected installed manifest. Explicit DLL_LOAD_DIR | SYSTEM32 and APPLICATION_DIR | SYSTEM32 remove current-directory/PATH searching. Program Files and ProgramData paths are not intended to be writable by standard users; installation ACL checks are part of real-machine acceptance.

Ownership requires both the protected GUID record and exact expected SCM binary path. Conflicting or ambiguous services fail closed. Legacy/foreign WireGuard resources are never adopted. Maintenance deletes only GUID-derived verified owned resources. Upstream ringlogger inherits the secret-store DACL and is not exposed by the GUI.

## Runtime review

WireGuardGetConfiguration returns keys in a native buffer. Only handshake/RX/TX scalars are copied; clearing uses the allocation capacity rather than the mutable API output length. Bounds and peer count are validated. Route probes use canonical parsed CIDRs and GUID-derived adapter names; arbitrary command input is rejected.

Split tests never install a default route. Policy first stops every losing mode/profile; any stop failure blocks all new starts. Foreign active adapters prevent automatic HomeVPN starts. Desired state survives exclusion/conflict; overrides are memory-only and keyed to network fingerprint.

## Release limits

This is an unsigned development package. No signing certificate is invented or downloaded. Per-user offline-hive autostart cleanup and remaining acceptance cases are tracked in docs/VALIDATION.md. Machine administrators can decrypt machine-scope DPAPI and alter trusted files by design; protection is against ordinary users, not a compromised administrator/SYSTEM account.

Report a suspected vulnerability privately to the repository maintainer before publishing keys, configuration or exploit details in a public issue.
