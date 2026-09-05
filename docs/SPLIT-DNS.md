# Optional home DNS in split mode

In the import wizard choose **Routing → Optional: Heimnetz-DNS**. For an existing profile use **Einstellungen → VPN-Verbindungen → Heimnetz-DNS**. Enter one DNS server IP and up to 16 home domains, for example `home.arpa` or `fritz.box`. These are examples, not real-network presets. Use complete names such as `nas.home.arpa`; no global search suffix for bare `nas` is added.

The DNS IP must be inside an existing split target CIDR. HomeVPN does not silently add routes. The configured domains and their subdomains use that server while the split service and adapter are active. Other names retain normal local Windows resolution. Full mode keeps the imported WireGuard DNS settings. Clearing the server field disables split DNS and clears its domain list.

This is conditional routing, not public-first fallback: Windows stops after a negative name response instead of necessarily trying another server ([Microsoft resolver behavior](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/dns-client-resolution-timeouts)). HomeVPN uses [Windows NRPT rules](https://learn.microsoft.com/en-us/powershell/module/dnsclient/add-dnsclientnrptrule) for selected namespaces, without a DNS proxy or physical-adapter DNS changes.

Saving an existing profile's DNS briefly stops its tunnel, requests same-account administrator elevation, writes protected metadata and resumes saved policy. Daily connect/disconnect needs no additional elevation. The DNS dialog applies immediately; cancelling the surrounding Settings window does not undo its successful change.

## Lifetime and ownership

The SYSTEM tunnel host launches an installed SYSTEM companion only for enabled split DNS. It verifies parent executable and protected profile ownership, waits for service/adapter, and creates an NRPT rule with exact and suffix namespaces. Redirected stdin closes on graceful parent completion or parent crash; the companion then removes its rule independently of the GUI. Reconnection, full-mode entry and verified profile removal also clean own stale rules. Protected file locks serialize profile lifetime and cross-profile NRPT writes.

An unpredictable tag in the protected record plus the GUID-derived display marker identify each own rule; removal requires both. Foreign rules are preserved and overlapping local rules block activation. The effective NRPT policy is checked after creation; a rule masked by Group Policy is not reported as active. Matching domains on parallel profiles are arbitrated like overlapping routes, retaining desired state.

A fixed Windows PowerShell script from the system directory receives data as JSON stdin; input is never executable interpolation. No keys enter DNS commands. A nonsecret HKLM heartbeat lets the normal GUI distinguish applied from unconfirmed DNS state. “Split-DNS aktiv” confirms NRPT application, not successful responses from the remote server.

## Validation and deferred checks

26 new automated cases cover disabled legacy profiles, metadata, IPv4/IPv6 route containment, invalid servers/domains, IDN, namespace scope, lifecycle eligibility and profile conflicts. The suite has 79 passing cases. PowerShell syntax and the installed Windows DNS module/schema were checked. Native Computer Use inspected the synthetic DNS dialog; `screenshots/fixture-split-dns.jpg` contains example data only.

The user confirmed basic operation of the needed VPN and deferred further live tests/hardening. Actual NRPT application/removal, private-name lookup, public-name comparison, parent-crash cleanup, domain-policy interaction and reboot with this feature remain **unverified**. Application-managed DoH/resolvers bypassing the Windows DNS client are outside NRPT control. Simultaneous failure of host and companion can leave an own rule until next profile start, full-mode entry or maintenance; broader crash recovery remains a hardening item. Installers are unsigned development builds.
