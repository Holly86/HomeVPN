# HomeVPN: Plan bis zum ersten stabilen Release

Stand: 2026-09-05. Ausgangspunkt: Version 0.2.6, Implementierungscommit `52b52859f4ce67e88f29acaef5e886e91adc5ac9`, [Draft-PR #2](https://github.com/Holly86/HomeVPN/pull/2).

Dieses Dokument ist der Arbeitsplan für die nächsten Entwicklungsschritte. Es erteilt keine Release-Freigabe und startet keine Änderungen am laufenden VPN. Status und Nachweise werden nach jedem Arbeitspaket aktualisiert. Die historischen Ergebnisse und Fehlversuche bleiben in [VALIDATION.md](VALIDATION.md) erhalten.

## Ziel und bereits belegter Stand

Ziel ist eine eigenständige Windows-VPN-Anwendung, die nach einmaliger Einrichtung im normalen Benutzerkontext zuverlässig arbeitet. Die separate WireGuard-Anwendung soll anschließend entfernt werden können, ohne HomeVPN-Konfigurationen zu verlieren. Der notwendige WireGuard-Treiber bleibt als Bestandteil der eingebetteten Runtime erforderlich.

Bereits belegt:

- 79 automatisierte Tests sowie lokale Release-, Native- und Installer-Builds erfolgreich; beide CI-Läufe des Implementierungscommits erfolgreich.
- Eigene GUID-Tunnelservices, private WireGuard-DLLs, geschützter DPAPI-Speicher und enge Service-Rechte eingerichtet und zuvor unter Windows geprüft.
- Reale Split-Verbindung mit Peer-Handshake sowie Connect/Disconnect und Routingwechsel nach Entfernen temporärer Adminrechte geprüft; Grenzen dieser Tests stehen in VALIDATION.md.
- Der Nutzer hat Version 0.2.6 installiert, seinen Heim-DNS konfiguriert und die funktionierende lokale Namensauflösung ausdrücklich bestätigt. Dies ist ein Nutzer-Praxistest, keine vollständige Messung sämtlicher DNS-Wege und Abbruchfälle.

Weitere Live-Prüfungen und Härtung wurden vom Nutzer auf die nächsten Schritte verschoben. Neue Komfortfunktionen werden gegenüber den folgenden Release-Arbeiten zurückgestellt.

## Reihenfolge und Freigaben

| Paket | Priorität | Abhängigkeit | Status | Ergebnis |
| --- | --- | --- | --- | --- |
| R1 – WireGuard-Ablösung und Reparatur | P0 | Ausgangspunkt 0.2.6 | Offen | Geprüfter Wechsel ohne separate WireGuard-App |
| R2 – DNS-/Tunnel-Ausfallsicherheit | P0 | Kann vor dem realen R1-Wechsel bearbeitet werden | Offen | Wiederherstellung nach Abbruch ohne verwaiste eigene Regeln |
| R3 – Netzwerk- und Schutzverhalten | P0 | R2; für Endabnahme auch R1 | Offen | Definiertes Verhalten bei Neustart, Netzwechsel und Ausfall |
| R4 – Installer und Bedienung | P0 | R1/R2 für vollständigen Cleanup | Offen | Geprüfte Installation, Reparatur, Upgrade und Entfernung |
| R5 – Sicherheits- und Release-Paketprüfung | P0 | R1–R4 | Offen | Geprüfter, signierter und nachvollziehbarer Release Candidate |
| R6 – Pilot und Veröffentlichung | P0 | R5 | Offen | Stabiles Release mit dokumentierter Abnahme |

P0 bedeutet: vor dem stabilen öffentlichen Release abschließen oder den betroffenen Funktionsumfang ausdrücklich aus dem Release entfernen. Ein fehlgeschlagener Test wird als Fehler mit Nachtest geführt, nicht als erledigt markiert. R1 kann zunächst in einer Test-VM entwickelt werden; der Wechsel auf dem genutzten PC erfolgt erst nach den relevanten R2-/R3-/R4-Prüfungen.

## R1 – Separate WireGuard-Anwendung sicher ablösen

**Konkreter Befund:** Der [offizielle WireGuard-Installer am verwendeten Upstream-Commit](https://raw.githubusercontent.com/WireGuard/wireguard-windows/4e6726c23ae9c5cb58e0c9910f3b7515621d133d/installer/customactions.c) sucht Dienste anhand von `WireGuardTunnel$` und plant deren Entfernung bei Deinstallation. Auch HomeVPN verwendet upstreambedingt dieses Präfix. Außerdem ruft der Installer die Treiberentfernung auf. Unabhängiger Betrieb bedeutet daher noch nicht, dass eine parallele WireGuard-Deinstallation folgenlos bleibt.

- [ ] Reproduzierbaren Koexistenz-/Deinstallationstest in einer Windows-Testumgebung erstellen und Auswirkungen auf eigene Dienste, Adapter, Treiber und DNS protokollieren.
- [ ] Reparaturpfad implementieren oder vervollständigen: ausschließlich nachgewiesen eigene Dienste aus geschützten Profilen wiederherstellen, Runtime prüfen und benötigten Adapter/Treiber wieder bereitstellen. GUI zeigt fehlende Dienste mit einer konkreten Reparaturaktion.
- [ ] GUIDs, Profilnamen, DNS-Einstellungen, Zielnetze, gewünschte Zustände und enge Benutzerrechte bei Reparatur erhalten. Wiederholte Reparatur erzeugt keine Duplikate und startet keinen Tunnel vor Policy-Prüfung.
- [ ] Lokale Sicherung und Rückweg vor dem Wechsel beschreiben und praktisch prüfen. Originalkonfigurationen bleiben außerhalb von Repository, Logs und öffentlichen Artefakten.
- [ ] Saubere Windows-Installation ohne vorherige WireGuard-App prüfen: HomeVPN installieren, importieren, Split-DNS verwenden, neu starten und ohne Adminrechte bedienen.
- [ ] Anschließend den Wechsel auf dem genutzten PC durchführen: HomeVPN trennen, WireGuard regulär deinstallieren, HomeVPN bei Bedarf reparieren, neu starten und die benötigte Verbindung inklusive DNS prüfen. UAC-/Sicherheitsdialoge bedient der Nutzer selbst.

**Abnahme:** Die separate WireGuard-App und ihr Manager sind entfernt. HomeVPN funktioniert nach Neustart im normalen Benutzerkontext einschließlich Split-DNS. Keine eigenen Profile verloren, keine fremden Ressourcen durch HomeVPN verändert; ein geprüfter Rückweg steht bereit. Erst dann wird der Wechsel auf diesem PC als abgeschlossen dokumentiert.

## R2 – DNS und Tunnel bei Abbrüchen zuverlässig bereinigen

**Bekannte Lücke:** Wenn Tunnelhost und DNS-Begleitprozess gleichzeitig ausfallen, kann eine eigene NRPT-Regel bis zum nächsten Start oder zur Wartung bestehen bleiben. Wiederherstellung darf nicht von einem anschließend noch lebenden GUI-Prozess abhängen.

- [ ] Verbindlichen DNS-Lebenszyklus definieren und implementieren: Aktivieren, Trennen, Netzverlust, Moduswechsel, Prozessabbruch und Neustart. Zielzeiten für Aktivierung und Cleanup festlegen und messen.
- [ ] Wiederanlauf erkennt und bereinigt verwaiste eigene Regeln anhand geschützter Ownership; fremde und per Gruppenrichtlinie vorgegebene Regeln bleiben unangetastet.
- [ ] Einzelnen und gleichzeitigen Absturz von GUI, Tunnelhost und DNS-Begleiter testen; auch Abbruch während eines NRPT-Schreibvorgangs und einer DNS-Konfigurationsänderung berücksichtigen.
- [ ] Schnelle wiederholte Connect/Disconnect- und Split/Full-Wechsel testen. Keine verspätete alte Cleanup-Aktion darf Regeln einer neuen Sitzung entfernen.
- [ ] Fehler beim Speichern, fehlende Berechtigungen, nicht effektive NRPT-Regeln und ausgefallenen Heim-DNS verständlich anzeigen; Zustände dürfen nicht fälschlich als erfolgreich gelten.
- [ ] Globale DNS-Suchliste und physische Adapter-DNS unverändert lassen. Wirkung von DNS-Caches und anwendungseigenem DoH prüfen und dokumentieren.

**Abnahme:** Nach jedem Test liegen nur die für den tatsächlichen Verbindungszustand vorgesehenen eigenen Regeln vor. Öffentliche Namen bleiben im Split-Modus lokal auflösbar; konfigurierte Heimnetz-Domänen werden bei aktiver Verbindung korrekt aufgelöst. DNS-Probleme erzeugen einen nachvollziehbaren Status. Keine dauerhafte DNS-Störung nach Abbruch, Reparatur oder Neustart.

## R3 – Netzwerkverhalten und ehrlicher Verbindungsstatus

- [ ] Normaler Windows-Start und Anmeldung ohne Adminrechte; Desired OFF/ON sowie Ausschlussregeln und Session Override prüfen.
- [ ] Standby/Aufwachen, LAN↔WLAN, SSID-/Subnetzwechsel, Netzverlust und Wiederkehr testen. Overrides müssen an der vorgesehenen Sitzungsgrenze verschwinden.
- [ ] Peer nicht erreichbar, DNS-Server nicht erreichbar und Internet kurzzeitig unterbrochen: bounded Wartezeiten, bedienbare UI und nachvollziehbare Wiederverbindung prüfen.
- [ ] Statusmodell unterscheiden: Dienst läuft, Adapter/Routen vorhanden, letzter bekannter Peer-Handshake, DNS-Richtlinie angewendet. Keine dauerhafte Erreichbarkeit aus einem alten Handshake ableiten.
- [ ] Split- und Full-Modus mit IPv4 und IPv6 messen. Für Full-Modus explizit festlegen, ob bei Tunnelausfall Verkehr blockiert wird und wie IPv6 bei IPv4-only-Profilen behandelt wird; Verhalten, UI und Tests müssen übereinstimmen.
- [ ] Zwei reale konfliktfreie Split-Profile gleichzeitig testen; CIDR-/DNS-Domänenkonflikte und Full-Exklusivität mit dokumentierter Priorität prüfen.

**Abnahme:** Die folgende Matrix ist auf dem Release Candidate ausgeführt; Abweichungen sind behoben oder führen zu einer ausdrücklich engeren Release-Unterstützung.

| Fall | Zu belegendes Ergebnis |
| --- | --- |
| Split, DNS deaktiviert | Nur Zielnetze getunnelt; normale lokale DNS-Auflösung |
| Split, DNS aktiviert | Heim-Domänen über Heim-DNS; öffentliche Namen lokal; Cleanup nach Trennen |
| Full, IPv4/IPv6 | Deklariertes Routing-/DNS-/Ausfallverhalten, keine unbeabsichtigte Umgehung |
| Anmeldung, Standby, Netzwechsel | Policy erneut korrekt; keine unzulässig übernommenen Overrides |
| Peer-/DNS-/Netzausfall | Verständlicher Status, UI reagiert, geprüfte Wiederherstellung |
| Mehrere Profile | Konfliktfreie Parallelität; konkurrierende Routen/DNS-Regeln werden verhindert |

## R4 – Installation, Upgrade und Bedienung abschließen

- [ ] Clean Install, Upgrade von 0.2.6, Reparatur und abgebrochene Installation testen; Rollback erhält nutzbare Profile und Rechte.
- [ ] Deinstallation mit Profilen behalten und mit Profilen löschen prüfen. Eigene Dienste, Autostart und DNS-Regeln müssen passend entfernt werden; fremde Ressourcen bleiben erhalten.
- [ ] Den zuvor fehlgeschlagenen Autostart-Cleanup mit dem aktuellen Benutzerkontext-Fix real nachtesten. MSI-Erfolg allein genügt nicht.
- [ ] Verhalten bei weiteren Benutzerkonten und nicht geladenen Benutzer-Hives festlegen; verzögertes Metadaten-Löschen transparent beschreiben und prüfen.
- [ ] Tray, Single Instance, Tastatur-/Fokusführung, kleine Laptop-Arbeitsfläche und relevante Dialoge bei 100/125/150 % prüfen, einschließlich Split-DNS und langer Fehlermeldungen.

**Abnahme:** Wiederholte Installations-/Entfernungszyklen lassen keine funktionsstörenden eigenen Dienste oder DNS-/Autostart-Reste zurück. Beibehaltene Profile sind ohne neuen Import wiederherstellbar. Die freigegebene UI-Matrix enthält keine blockierenden Bedien- oder Layoutfehler.

## R5 – Sicherheit, Signierung und reproduzierbarer Release Candidate

- [ ] Abschließendes Review von Setup-Pipe, privilegierten Eingaben, Dienst-/Dateirechten, Ownership, Reparse Points, temporären Dateien und Fehler-/Rollback-Pfaden durchführen. Neue DNS-Komponenten einbeziehen.
- [ ] DLL-Suchpfade und native .NET-Single-File-Extraktion im erhöhten Benutzer- und SYSTEM-Kontext prüfen; tatsächlich unterstützte Windows-Versionen und Update-Voraussetzungen festlegen.
- [ ] Schlüssel-/Konfigurationsscan über versionierte Dateien und veröffentlichte Artefakte ausführen. Diagnoseexporte müssen ohne Private/Preshared Keys und vollständige Konfiguration auskommen.
- [ ] Offizielle Upstream-Pins, Hashes, Lizenzen/Notices und unterstützte .NET-/Windows-Versionen prüfen; Prozess für Sicherheitsupdates und erneute Paketierung dokumentieren.
- [ ] EXE/MSI/Bootstrapper signieren und mit Zeitstempel versehen; Zertifikat bzw. Signierdienst und dessen Zugang werden durch den Maintainer bereitgestellt. Signaturprüfung in den Release-Ablauf aufnehmen.
- [ ] Paket aus dem vorgesehenen Commit erstellen; Produktversion, eingebettete Commit-Information, Tag und Release Notes müssen zusammenpassen. Artefakt-Hashes veröffentlichen.
- [ ] Einen einfachen, verifizierten manuellen Updateweg und Rückweg dokumentieren. Ein automatischer Updater ist für das erste Release nicht erforderlich.

**Abnahme:** Keine offenen kritischen/hohen Review-Befunde; CI für den tatsächlich veröffentlichten Commit grün. Signierte, überprüfte Artefakte mit nachvollziehbaren Versionen und vollständigen Lizenztexten liegen vor. Der [Microsoft-Leitfaden zu SmartScreen und Signierung](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) ist berücksichtigt; Signierung wird nicht als Garantie sofortiger Reputation dargestellt.

## R6 – Pilotbetrieb und Veröffentlichung

- [ ] Einen Release Candidate auf dem genutzten PC und mindestens einer sauberen Testinstallation abnehmen.
- [ ] Als Pilotfenster fünf Arbeitstage normalen Betrieb einplanen, einschließlich täglichem Neustart oder An-/Abmeldung und mehreren Netzwechseln. Beobachtungszeit und Ergebnisse tatsächlich festhalten.
- [ ] Alle P0-Abnahmen verlinken, verbleibende Einschränkungen in Release Notes und Bedienhinweisen aufführen und Maintainer-Freigabe dokumentieren.
- [ ] Draft-PR final prüfen und mergen; Release aus dem freigegebenen Commit taggen, signierte geprüfte Artefakte veröffentlichen und Rückmeldungs-/Fehlerkanal angeben.

**Abnahme:** Keine ungeklärten wiederholbaren Ausfälle in den freigegebenen Szenarien. Der Nutzer hat den Betrieb ohne separate WireGuard-App bestätigt. Veröffentlichung und dokumentierter Funktionsumfang entsprechen den tatsächlich geprüften Artefakten.

## Nachweise und Fortschritt pflegen

Für jeden Nachweis festhalten: Paket-ID (R1–R6), Version/Commit, Windows-Version, Benutzerrechte, Testart, Ausgangszustand, Schritte, erwartetes/tatsächliches Ergebnis und verbleibende Abweichungen. IPs, Domänen und Profilnamen in öffentlichen Nachweisen bei Bedarf durch Beispiele ersetzen; niemals Schlüssel oder vollständige Konfigurationen ablegen.

Testarten bleiben getrennt: automatisiert, Windows-API-Integration, synthetische UI, realer Windows-Test, echter Peer/DNS-Test und Nutzerbestätigung. Ein erfolgreicher Nutzerfall ersetzt keine vollständige Matrix. Maßgebliche Ergebnisse stehen in [VALIDATION.md](VALIDATION.md); DNS-Details in [SPLIT-DNS.md](SPLIT-DNS.md), Sicherheitsentscheidungen in [SECURITY.md](../SECURITY.md).

**Nächster konkreter Schritt:** R1 in einer Testumgebung reproduzieren und daraus den Reparatur-/Wechselpfad entwickeln. Vor einer Deinstallation auf dem genutzten PC müssen Backup/Rückweg, eigener Service-Restore und DNS-Cleanup überprüfbar funktionieren.
