# Carpoolr im Staging testen

Die Anwendung verwendet Vue 3, TypeScript und Vite. Die Oberfläche ist deutsch und mobil nutzbar. Alle Daten kommen von der API; es gibt keine erfundenen Fahrten, Adressen oder Fahrzeugwerte im Frontend.

## Person auswählen und Profil ergänzen

Oben ausdrücklich eine der vom Server angebotenen Testpersonen auswählen. Es gibt keinen Login und keine automatisch gewählte Person. Eine früher gewählte Person wird nur wiederhergestellt, wenn sie weiterhin im Serverkatalog existiert. Gespeichert wird im Browser nur diese Auswahl, nicht die Abholadresse oder ein Token.

Die Auswahl bestimmt, wessen Profil und Fahrten du gerade bearbeitest. Das ist eine Simulation für den Testbetrieb. Unter **Mein Profil** den Namen und die vollständige Abholadresse speichern. Optional ein Auto mit Bezeichnung, Mitfahrerplätzen ohne Fahrer, Antrieb, Verbrauch pro 100 km und Preis pro Liter bzw. kWh anlegen.

Dezimalwerte dürfen mit Komma eingegeben werden. Die API erhält exakte Dezimalstrings mit Punkt und höchstens vier Nachkommastellen. Adressen können mehrzeilig eingegeben werden; Zeilenumbrüche werden vor dem Speichern zu Leerzeichen normalisiert.

## Eine Fahrt anbieten

1. **Fahrt anbieten** öffnen. Dafür muss ein Profil mit Auto gespeichert sein.
2. Vollständige Start- und Zieladresse, Abfahrt, Mitfahrerplätze und die selbst ermittelte Gesamtstrecke in Kilometern eingeben.
3. Für regelmäßige Angebote **Wöchentlich wiederholen** aktivieren, ersten/letzten möglichen Tag, Wochentage und Uhrzeit festlegen.
4. Speichern. Einzelne Angebote öffnen direkt ihre Fahrtdetails; Serien erscheinen unter **Meine Angebote**.

Alle Uhrzeiten gelten ausdrücklich in **Europe/Berlin**, unabhängig von der Zeitzone des Browsers. Sommer-/Winterzeit wird berücksichtigt. Nicht existierende oder doppeldeutige Uhrzeiten bei der Zeitumstellung werden abgelehnt. Eine Serie erzeugt einzelne Fahrten; eine Rückfahrt ist ein separates Angebot.

Die Strecke wird **manuell** angegeben. Automatische Routenplanung, Geocoding und Routenoptimierung sind noch nicht angebunden. Das Frontend erfindet keine Entfernungen.

## Mitfahren und Anfragen bearbeiten

Unter **Fahrten finden** nach Datum suchen. Optionale Start-/Zielfilter vergleichen vollständige Adressen genau. Ergebnisse enthalten nur zukünftige, geplante Fahrten. Weitere Ergebnisse lassen sich nachladen.

In den Fahrtdetails **Einen Platz anfragen** wählen. Ein vollständiges eigenes Profil mit Abholadresse ist erforderlich. Die Anfrage reserviert zunächst keinen Platz. Der Fahrer entscheidet unter **Mitfahranfragen** über **Zusagen** oder **Ablehnen**. Nach einer Zusage wird die Abholadresse für ihn sichtbar und der Platz belegt.

Unter **Meine Anfragen** lassen sich eigene Anfragen und Mitfahrten verfolgen. Vor der Abfahrt kann eine Anfrage bzw. Teilnahme zurückgezogen werden. Nach Ablehnung oder Rückzug kann erneut angefragt werden, solange die Fahrt noch offen ist und Plätze frei sind. **Aktualisieren** lädt in den Fahrtdetails den aktuellen Stand; es gibt noch keine Live-Benachrichtigungen.

Der Fahrer sieht einen Maps-Link mit vollständiger Start-/Zieladresse und bestätigten Abholstopps. Die angezeigte Reihenfolge wird nicht als optimiert ausgegeben. Der Link öffnet die externe Karte erst bei einem Klick.

## Absagen und Abrechnen

Der Fahrer kann in den Fahrtdetails ein einzelnes Angebot bzw. einen einzelnen Serientermin absagen. Unter **Meine Angebote** kann er alle zukünftigen, noch geplanten Termine einer Serie absagen. Abgefahrene oder abgeschlossene Fahrten bleiben erhalten.

Die Kostenvorschau verwendet `Strecke × Verbrauch / 100 × Energiepreis`, gleichmäßig geteilt durch Fahrer und bestätigte Mitfahrer. Beträge werden vom Server berechnet und als ganze Cent übertragen. Die Darstellung erhält auch bei großen `int64`-Werten die Centgenauigkeit.

Nach der geplanten Abfahrt kann der Fahrer die Fahrt abschließen. Dabei die tatsächliche gesamte Strecke einschließlich Abholwegen, Verbrauch und Energiepreis prüfen. **Fahrt abschließen und Kosten festschreiben** erzeugt die unveränderliche Abrechnung mit individuellen Anteilen; Rundungsreste können einen Cent Unterschied erzeugen.

Ein Mitfahrer meldet den eigenen Anteil mit **Als bezahlt melden** als beglichen. Der Fahrer bestätigt den Zahlungseingang oder lehnt die Meldung ab. Nach Ablehnung kann erneut gemeldet werden. Es wird kein Geld über die App überwiesen und kein Zahlungsanbieter angebunden.

## Technischer Aufbau

- [`session.ts`](../apps/web/src/state/session.ts) lädt `GET /demo/users`, prüft die gespeicherte Auswahl gegen diesen Katalog und verwaltet das aktuelle Profil.
- [`application.ts`](../apps/web/src/api/application.ts) erstellt Clients für alle fünf gRPC-Services. Die explizit ausgewählte Person wird pro Clientinstanz im Header `X-Carpoolr-Demo-User` festgehalten; ein laufender Aufruf wechselt beim Umschalten nicht die Identität.
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

Die Unit-Tests prüfen unter anderem explizite Personenauswahl, verworfene veraltete Identitäten, binäres gRPC-Web, Fehlertrailer, Abbruch/Deadlines, Berliner Zeitumstellung und exakte Centdarstellung. Die Oberfläche muss zusätzlich im echten Browser gegen die laufende API geprüft werden; die Unit-Tests ersetzen diesen Durchstich nicht.
