# Embedded WireGuard

The embedded backend is implemented. HomeVPN no longer invokes wireguard.exe or relies on WireGuardManager. See [pinned upstream evidence](UPSTREAM-PINS.md) for signatures, DLL paths, licenses, service configuration, encrypted configuration lifecycle and diagnostics.

HomeVPN builds the official unmodified embeddable-dll-service at the pinned WireGuard Windows 1.1 commit and ships the official WireGuardNT 1.1 amd64 DLL. There is no third-party WireGuard NuGet wrapper. Build-time archive/output hashes and installed runtime hashes are checked.

LocalSystem runs HomeVPN.TunnelService.exe --service GUID split/full from the protected install directory. It validates ownership and passes the GUID-derived encrypted configuration path to WireGuardTunnelService. The service DACL grants the importing account only query/start/stop/interrogate (0xB4), and grants full control to SYSTEM/Administrators. Services use demand start, Nsi/TcpIp dependencies and unrestricted service SID.

Integration acceptance must distinguish SCM Running, adapter presence, target routes, actual runtime modules, and peer handshake. The installer test uses only split routes and stops the tested service before returning to policy. Multiple own split services can coexist only when CIDRs do not overlap; full mode is exclusive. Legacy/fremde services are untouched.
