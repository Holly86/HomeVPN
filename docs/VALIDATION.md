# Validation record — in progress

Evidence is categorized: synthetic UI and policy fixtures do not count as real peer tests. No real configuration, endpoint, key or personal route is committed.

## Automated

79 xUnit cases pass on Windows/.NET 8.0.30. Coverage includes IPv4/IPv6 CIDR containment and overlap, split/full rendering, parser validation, immutable identity, minimal service ACL masks, machine-DPAPI roundtrip/tamper, secret-free metadata, legacy migration, desired/effective arbitration, per-profile exclusions, session override, full exclusivity, non-overlapping split plans, stop-before-start and stop-failure blocking, named-pipe ACL/framing, settings persistence, corruption handling and invalidation after machine-wide purge. Service policy interactions use fakes; DPAPI and pipe cases use actual Windows APIs.

Release solution builds without warnings. Repeated official native builds match native-hashes.json. WiX MSI and Burn builds pass. VisualHarness and WindowsProbe are test-only executables excluded from the installer.

## Real Windows / real WireGuard peer

- Clean per-machine 0.2.0 installation succeeded with MSI result 0 under Program Files/HomeVPN.
- 0.2.1 upgrade fixed the elevated named-pipe import; the real local config then provisioned a GUID profile and both manual-start services.
- Real split setup test confirmed runtime module path, service Running, adapter, expected target routes and a successful peer handshake. Service stopped after the test. Screenshot: setup-success.jpg.
- After the user explicitly removed temporary Make Me Admin rights, a newly started normal GUI connected, disconnected, switched to full mode, switched back to split, and disconnected again without elevation.
- Independent WindowsProbe verified query/start/stop/interrogate access in both services, while CHANGE_CONFIG, WRITE_DAC and DELETE were denied. The normal user cannot read the protected ProgramData profile directory.
- Split mode had the expected target route and no own default route. Full mode had IPv4 /0 with only the full service running; HTTPS returned 200. The imported profile is IPv4-only: this does not establish IPv6 tunneling or absence of IPv6 bypass.
- SCM registry confirmed own-process LocalSystem, manual start, Nsi/TcpIp dependencies, unrestricted service SID and quoted fixed HomeVPN host paths. Program Files ACLs permit ordinary users read/execute only.
- Renamed the imported profile to Zuhause without changing its GUID. Exclusion on the current network paused desired-ON VPN; manual override connected it. A fresh --background process preserved desired state and cleared the override. Single-instance activation brought the same GUI forward.
- Upgrade 0.2.1 to 0.2.2 retained GUID, display name and desired state and recreated both own services. No reimport was required.
- Existing foreign WireGuard installation and service definitions remain intact. The existing active tunnel was stopped only after the user's explicit authorization for connection tests.

## Computer Use visual checks

Installed first-run/import pages (name, routes, network, setup), successful setup result, main disconnected/connected/excluded/override states, routing controls and settings were inspected through native Computer Use.

Actual Windows display scaling was changed to 100%, 125%, then restored to its original 150%, with resolution remaining 2400x1600. The installed paused main window remained usable at all three settings. Screenshots main-100.jpg, main-125.jpg and main-150.jpg document this limited DPI check; it is not a claim that every dialog was tested at every DPI.

Synthetic VisualHarness checks at 150% covered connecting, connected and parallel profile display, open-Wi-Fi warning, route conflict, missing service, access denied, and the expanded invalid-config error dialog. A 900x480-DIP window approximates a 1366x768 laptop work area at 150%; 23 profiles and a long name exposed a collapsed profile list with an exclusion banner. Compact window spacing was added and retested: primary actions, a scrollable profile row and footer now remain usable. Only dynamic profile content scrolls. Synthetic screenshots are explicitly named fixture-*.

The Burn maintenance page was visually checked with profile retention selected by default. Real 0.2.2 uninstall with KEEP_PROFILES=1 returned 0, removed the app and own services, and kept metadata/secrets. Reinstalling 0.2.3 restored the same GUID and both manual services; WindowsProbe again passed the minimal-rights and protected-store checks. The real split test on 0.2.3 confirmed both tunnel.dll and wireguard.dll from the private runtime, service, adapter, routes and peer handshake.

Real 0.2.3 uninstall with KEEP_PROFILES=0 returned 0; an elevated probe confirmed zero protected profile directories and zero own services. Both uninstall runs left the HKCU Run value behind: this is a failed acceptance check, despite MSI reporting success. Direct elevated execution of the same cleanup code successfully removed it. An MSI-owned HKCU component in 0.2.4 also failed this check despite successful RegRemoveValue logging, so that approach was removed. Version 0.2.5 explicitly runs user cleanup impersonating the initiating MSI user and rejects SYSTEM or a different SID. Its real uninstall check remains pending. Machine purge generation was successfully written by 0.2.4; normal-user metadata invalidation is unit tested and awaits the final GUI restart.

## Failed attempts and fixes

Initial NuGet restore lacked a source; explicit NuGet.Config fixed it. A compiler bootstrap install was rejected by automatic tool approval review, so packaging uses WiX as a .NET tool. WireGuardNT license path and WiX extension/component definitions were corrected after build errors. VisualHarness initially failed inherited App BAML resource loading; shared ResourceDictionary fixed startup. An additional fixture compile failed for a missing System.IO import and was corrected.

The first installer exposed duplicate Burn/MSI license screens. Subsequent bundles suppress nested MSI UI, use German text and offer profile retention. The first real import failed on .NET 8 CurrentUserOnly TokenOwner mismatch across UAC; explicit account-SID pipe ownership/ACL and server/client verification fixed it. A sequential zero-buffer pipe unit test deadlocked; concurrent endpoints fixed the test. Opening a bundle during rebuilds locked its output; closing the setup and its cancellation confirmation allowed the final bundle build to pass. An attempted RemoveRegistryValue uninstall attribute was rejected by the WiX schema; the subsequent owned RegistryValue experiment also failed real cleanup and was replaced. A 0.2.5 local-variable naming conflict caused CS0136; renaming it restored the warning-free build and all 53 passing tests.

## User acceptance and DNS addition

The user confirmed basic operation of the one required VPN connection and explicitly deferred further live checks and hardening. Version 0.2.6 adds optional per-profile split DNS after the user chose domain-based NRPT routing instead of public-first fallback. All 79 tests, warning-free Release build, native hash verification, MSI and Burn builds pass. The DNS dialog and invalid-server validation were visually inspected in the fixture. The 26 new DNS cases and unverified real DNS behaviors are documented in SPLIT-DNS.md. No live DNS policy was applied during this addition.

## Deferred validation

Final installer autostart cleanup and settings reset, tray interactions, and the additional DNS live checks remain deferred. Pushed-branch CI is recorded below. A real Windows logoff/reboot, physical network switch and two separate live remote peers have not been tested. Startup command, override restart behavior and policy/network-change unit cases are separately evidenced above. Unsigned development installers are not a signed production release.
