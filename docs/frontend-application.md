# Carpoolr im Staging testen

Die Anwendung verwendet Vue 3, TypeScript und Vite. Die Oberfläche ist deutsch und mobil nutzbar. Alle Daten kommen von der API; es gibt keine erfundenen Fahrten, Adressen oder Fahrzeugwerte im Frontend.

Carpoolr kann als PWA auf Mobilgeräten installiert werden. Installation, automatische sichere Updates und Offline-Grenzen sind unter [PWA](pwa.md) beschrieben.

Unter **Farbschema** am Seitenende stehen **System**, **Hell** und **Dunkel** zur Auswahl. Standardmäßig folgt die App der Geräteeinstellung; eine eigene Auswahl bleibt auf dem Gerät erhalten. Dekorative Motto- und Werbetexte wurden entfernt; Überschriften und Hilfetexte beschreiben Funktionen und erforderliche Eingaben.

Die **Startseite** zeigt die nächste eigene oder bestätigte Fahrt, offene Aufgaben und einen Kostenüberblick. Die mobile Hauptnavigation enthält **Start**, **Suchen**, **Meine Fahrten**, **Kosten** und **Profil**. Unter **Meine Fahrten** wechseln die Tabs zwischen Angeboten, Mitfahrten und eingegangenen Anfragen. Auf großen Bildschirmen wird dieselbe Struktur als Seitenleiste dargestellt. Gestaltungsregeln stehen im [Design-System](design-system.md).

## Anmelden oder Testperson auswählen

Die App prüft beim Start über `GET /api/auth/session` die ausdrücklich konfigurierte Betriebsart. Im bisherigen Staging ist weiterhin der **Testmodus** aktiv. Ein Carpoolr-Provider in Authentik ist noch nicht angelegt; die [Einrichtung für die echte Anmeldung](authentication.md) ist vorbereitet und wird getrennt aktiviert.

Im Testmodus oben ausdrücklich eine der vom Server angebotenen Testpersonen auswählen. Es gibt keine automatisch gewählte Person. Eine frühere Auswahl wird nur wiederhergestellt, wenn sie weiterhin im Serverkatalog existiert. Die Auswahl bestimmt, wessen Profil und Fahrten gerade bearbeitet werden; sie ist eine Simulation und keine Anmeldung.

Im ausdrücklich aktivierten **OIDC-Modus** ersetzt **Anmelden** die Testpersonenauswahl. Authentik prüft die Freischaltung für die Carpoolr-Application; anschließend nutzt die App eine eigene HttpOnly-Cookie-Sitzung und einen benutzergebundenen CSRF-Token. OAuth-Tokens werden nicht im Browser gespeichert. **Abmelden** beendet die Carpoolr-Sitzung, lässt aber andere Authentik-Anwendungen angemeldet. Nach Ablauf oder Wechsel der Sitzung bleiben ungespeicherte Eingaben zunächst sichtbar; vor einer erneuten Anmeldung beziehungsweise Abmeldung fragt die App nach dem Verwerfen von Änderungen.

Unter **Mein Profil** den Namen und die vollständige Abholadresse speichern. Optional ein Auto mit Bezeichnung, Mitfahrerplätzen ohne Fahrer, Antrieb, Verbrauch pro 100 km und Preis pro Liter bzw. kWh anlegen. Reale Benutzer erhalten eigene Profile; Demo-Daten werden nicht automatisch übertragen. Lokal gespeichert werden das Farbschema und gegebenenfalls die Testpersonenauswahl, keine Abholadresse oder OAuth-Tokens.

Dezimalwerte dürfen mit Komma eingegeben werden. Die API erhält exakte Dezimalstrings mit Punkt und höchstens vier Nachkommastellen. Adressen können mehrzeilig eingegeben werden; Zeilenumbrüche werden vor dem Speichern zu Leerzeichen normalisiert.

## Eine Fahrt anbieten

1. **Fahrt anbieten** öffnen. Dafür muss ein Profil mit Auto gespeichert sein.
2. Vollständige Start- und Zieladresse, Abfahrt, Mitfahrerplätze und die selbst ermittelte Gesamtstrecke in Kilometern eingeben.
3. Für regelmäßige Angebote **Wöchentlich wiederholen** aktivieren, ersten/letzten möglichen Tag, Wochentage und Uhrzeit festlegen.
4. Speichern. Einzelne Angebote öffnen direkt ihre Fahrtdetails; Serien erscheinen unter **Meine Angebote**.

Alle Uhrzeiten gelten ausdrücklich in **Europe/Berlin**, unabhängig von der Zeitzone des Browsers. Sommer-/Winterzeit wird berücksichtigt. Nicht existierende oder doppeldeutige Uhrzeiten bei der Zeitumstellung werden abgelehnt. Eine Serie erzeugt einzelne Fahrten; eine Rückfahrt ist ein separates Angebot.

Die Strecke wird **manuell** angegeben. Automatische Routenplanung, Geocoding und Routenoptimierung sind noch nicht angebunden. Das Frontend erfindet keine Entfernungen.

## Mitfahren und Anfragen bearbeiten

Unter **Suchen** nach Datum suchen. Optionale Start-/Zielfilter vergleichen vollständige Adressen genau. Ergebnisse enthalten nur zukünftige, geplante Fahrten. Weitere Ergebnisse lassen sich nachladen.

In den Fahrtdetails **Einen Platz anfragen** wählen. Ein vollständiges eigenes Profil mit Abholadresse ist erforderlich. Die Anfrage reserviert zunächst keinen Platz. Der Fahrer entscheidet unter **Mitfahranfragen** über **Zusagen** oder **Ablehnen**. Nach einer Zusage wird die Abholadresse für ihn sichtbar und der Platz belegt.

Unter **Meine Mitfahrten** lassen sich eigene Anfragen und Mitfahrten verfolgen. Vor der Abfahrt kann eine Anfrage bzw. Teilnahme zurückgezogen werden. Nach Ablehnung oder Rückzug kann erneut angefragt werden, solange die Fahrt noch offen ist und Plätze frei sind.

Unter **Eingegangene Anfragen** bearbeiten Fahrer die Anfragen zu allen eigenen Angeboten zentral. Standardmäßig werden offene, noch bearbeitbare Anfragen angezeigt. Der Filter kann auch bearbeitete Anfragen und die Historie einblenden. Zusagen und Ablehnen sind direkt in der Übersicht möglich; Abholadressen bleiben bis zur Zusage verborgen.

**Meine Angebote** zeigt zuerst die nächsten geplanten Abfahrten, danach sonstige zukünftige Angebote und zuletzt vergangene Fahrten mit den jüngsten zuerst. Auch die Fahrtsuche beginnt mit der nächsten Abfahrt.

## Offene Aufgaben und Kostenübersicht

Die Glocke im Kopfbereich zählt offene Mitfahranfragen und Zahlungsmitteilungen, die der ausgewählte Fahrer bestätigen oder ablehnen muss. Sie zeigt eine begrenzte Vorschau und Links zu den vollständigen Übersichten. Bearbeitete, zurückgezogene oder wegen Abfahrt/Absage nicht mehr bearbeitbare Anfragen verschwinden aus der Aufgabenliste. Es gibt keinen separaten Gelesen-Status und keine externen Benachrichtigungen.

Die Glocke lädt beim Öffnen, nach eigenen Aktionen, beim Wechsel der Ansicht und bei Rückkehr ins Fenster neu. Solange die Seite sichtbar ist, wird zusätzlich alle 30 Sekunden aktualisiert. Bei einem Fehler wird der letzte Stand ausdrücklich als möglicherweise veraltet gekennzeichnet.

Unter **Meine Kosten** stehen eigene Kostenanteile einschließlich des Fahreranteils sowie offene, gemeldete und bestätigte Zahlungen. Fahrer sehen außerdem die Erstattungen ihrer Mitfahrer und können Zahlungseingänge direkt bestätigen oder ablehnen. Mitfahrer sehen ausschließlich ihren eigenen Anteil; Fahrer sehen die Anteile ihrer Mitfahrer.

Die Gesamtsummen werden serverseitig über alle festgeschriebenen Abrechnungen berechnet, unabhängig von der aktuell geladenen Seite. Kostenvorschauen geplanter Fahrten zählen nicht hinein. Gemeldete Erstattungen sind Teil der ausstehenden Summe, bis der Fahrer den Eingang bestätigt. Darunter sind Strecke, Datum und Zahlungsstände je Fahrt aufgeschlüsselt.

Der Fahrer sieht einen Maps-Link mit vollständiger Start-/Zieladresse und bestätigten Abholstopps. Die angezeigte Reihenfolge wird nicht als optimiert ausgegeben. Der Link öffnet die externe Karte erst bei einem Klick.

## Absagen und Abrechnen

Der Fahrer kann in den Fahrtdetails ein einzelnes Angebot bzw. einen einzelnen Serientermin absagen. Unter **Meine Angebote** kann er alle zukünftigen, noch geplanten Termine einer Serie absagen. Abgefahrene oder abgeschlossene Fahrten bleiben erhalten.

Die Kostenvorschau verwendet `Strecke × Verbrauch / 100 × Energiepreis`, gleichmäßig geteilt durch Fahrer und bestätigte Mitfahrer. Beträge werden vom Server berechnet und als ganze Cent übertragen. Die Darstellung erhält auch bei großen `int64`-Werten die Centgenauigkeit.

Nach der geplanten Abfahrt kann der Fahrer die Fahrt abschließen. Dabei die tatsächliche gesamte Strecke einschließlich Abholwegen, Verbrauch und Energiepreis prüfen. **Fahrt abschließen und Kosten festschreiben** erzeugt die unveränderliche Abrechnung mit individuellen Anteilen; Rundungsreste können einen Cent Unterschied erzeugen.

Ein Mitfahrer meldet den eigenen Anteil mit **Als bezahlt melden** als beglichen. Der Fahrer bestätigt den Zahlungseingang oder lehnt die Meldung ab. Nach Ablehnung kann erneut gemeldet werden. Es wird kein Geld über die App überwiesen und kein Zahlungsanbieter angebunden.

## Technischer Aufbau

- [`session.ts`](../apps/web/src/state/session.ts) prüft `GET /auth/session` und verwaltet Betriebsart, aktuelle Identität, Profil und einen ausschließlich im Arbeitsspeicher gehaltenen CSRF-Token. Nur im ausdrücklich gemeldeten Demo-Modus lädt es `GET /demo/users` und prüft eine gespeicherte Testperson gegen diesen Katalog.
- [`application.ts`](../apps/web/src/api/application.ts) erstellt Clients für alle sechs gRPC-Services. Im Demo-Modus enthalten Aufrufe `X-Carpoolr-Demo-User`; im OIDC-Modus dieselbe Origin, die Sitzungscookies und `X-CSRF-Token`. Die pro Client festgehaltene Identität muss weiterhin der aktiven Identität entsprechen. Authentifizierungsfehler und ein ungültiger CSRF-Token markieren die Sitzung als abgelaufen beziehungsweise gewechselt; Änderungen werden nicht automatisch erneut gesendet.
- [`grpc.ts`](../apps/web/src/api/grpc.ts) verwendet binäres gRPC-Web, Standardfehlercodes, Deadlines und Abbruchsignale. Generierte Typen kommen ausschließlich aus den lokalen `.proto`-Dateien; siehe [Codegenerierung](frontend-grpc.md).
- [`useOperation`](../apps/web/src/composables/useOperation.ts) bündelt Lade-/Fehlerzustände, verhindert doppelte gleichzeitige Aktionen einer Ansicht und bricht Aufrufe beim Verlassen ab.
- [`format.ts`](../apps/web/src/lib/format.ts) konvertiert Berliner Kalenderzeiten und normalisiert Dezimalwerte. [`labels.ts`](../apps/web/src/lib/labels.ts) formatiert Centbeträge ohne Gleitkomma-Konvertierung.
- Die Ansichten unter `src/views` bilden Suche, Profil, Angebot, eigene Angebote, eigene Anfragen und Fahrtdetails ab. Aktionsschaltflächen berücksichtigen die vom Server gelieferten Berechtigungen; die API prüft diese unabhängig davon erneut.

Für eine neue Fahrt wird eine Request-UUID erzeugt. Bei einem Retry mit unveränderten Eingaben bleibt sie erhalten. Geänderte Eingaben erhalten eine neue UUID. Erfolgreiche Änderungen werden anschließend erneut vom Server gelesen; die Oberfläche simuliert keinen Erfolg mit lokalen Datensätzen.

## Lokal prüfen

Die API mit Datenbank, angewendeten Migrationen und ausdrücklich aktiviertem Demo-Modus starten; die genaue Serverkonfiguration steht im Repository-README. Dann im Repository-Stamm:

```powershell
bun install --cwd apps/web --frozen-lockfile
bun run --cwd apps/web proto:check
bun run --cwd apps/web format:check
bun run --cwd apps/web test
bun run --cwd apps/web build
bun run --cwd apps/web dev
```

Das Frontend verwendet lokal `VITE_API_BASE_URL` oder andernfalls `http://localhost:5000`. Im bestehenden nginx-Proxy lautet die Basis `/api`. Für gRPC-Web sind die serverseitig konfigurierten CORS-Origins und gRPC-Statusheader relevant; bei gleichem Website-Origin im Cluster ist kein Cross-Origin-Aufruf nötig.

Prettier ist als Entwicklungswerkzeug exakt gepinnt. Zum Formatieren in `apps/web` `bun node_modules/prettier/bin/prettier.cjs --write src scripts` ausführen. Generierte protobuf-Dateien bleiben davon ausgeschlossen.

Die Unit-Tests prüfen unter anderem Betriebsarten und explizite Personenauswahl, verworfene veraltete Identitäten, Cookie-/CSRF-Metadaten, Farbschema, binäres gRPC-Web, Fehlertrailer, Abbruch/Deadlines, Berliner Zeitumstellung und exakte Centdarstellung. Die Oberfläche muss zusätzlich im echten Browser gegen die laufende API geprüft werden; die Unit-Tests ersetzen diesen Durchstich nicht. Ein erfolgreicher Test gegen einen simulierten OIDC-Provider ersetzt keine Prüfung am noch einzurichtenden Authentik-Provider.
