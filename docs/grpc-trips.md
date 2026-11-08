# TripService als Vorlage

Die API stellt sechs gRPC-Services für Fahrten, Profile, Mitfahranfragen, wöchentliche Serien, Abrechnung und die persönliche Übersicht bereit. `seats_total` bezeichnet angebotene Mitfahrerplätze ohne Fahrer; `available_seats` berücksichtigt bestätigte Mitfahrer. Das derzeitige Staging verwendet weiterhin ausdrücklich gewählte Testpersonen; die echte Anmeldung mit Authentik ist separat vorbereitet. Den fachlichen Ablauf erklären die [Anwendungsanleitung](frontend-application.md) und der [bestätigte Umfang](application-scope.md).

## Aufbau und Lesereihenfolge

1. [`trips.proto`](../apps/api/Protos/trips/v1/trips.proto): versionierter Vertrag `carpoolr.trips.v1`, Request-/Response-Nachrichten, Zeitpunkte und Statuswerte. C#-Typen werden beim Build erzeugt; generierte Dateien nicht bearbeiten. Feldnummern bleiben stabil; entfernte Nummern später mit `reserved` sperren.
2. [`TripGrpcService`](../apps/api/Grpc/TripGrpcService.cs): Transportadapter. Er prüft UUID-/Timestamp-Formate, liest die aktuelle Identität, übersetzt Nachrichten und reicht Abbruchsignale weiter. Er enthält keine Datenbankabfragen.
3. [`TripApplicationService`](../apps/api/Features/Trips/TripApplicationService.cs): Geschäftsregeln, Normalisierung, Eigentümerprüfung und idempotente Änderungen. Die Klasse kennt keine protobuf- oder HTTP-Typen.
4. [`CarpoolrDbContext`](../apps/api/Data/CarpoolrDbContext.cs): PostgreSQL-Abbildung, Indizes und Constraints. Ein scoped DbContext pro Aufruf; Leseabfragen verwenden `AsNoTracking`. Ein zusätzliches generisches Repository ist hier nicht nötig.
5. [`GrpcExceptionInterceptor`](../apps/api/Grpc/GrpcExceptionInterceptor.cs): zentrale Übersetzung erwarteter Fehler. Unerwartete Fehler liefern eine allgemeine Nachricht mit `trace-id`; Details stehen im Serverlog.
6. [`Program.cs`](../apps/api/Program.cs): Dependency Injection, Nachrichtenlimits, CORS und die Reihenfolge Routing → gRPC-Web → CORS → konfigurierte öffentliche Origin → Authentifizierung → Cookie-/CSRF-Prüfung → Autorisierung.

Für einen weiteren Service zuerst Vertrag und Geschäftsregeln ergänzen, dann Adapter und Integrationstests. Gemeinsame Logik gehört in die Anwendungsschicht, damit auch spätere Controller oder SignalR-Aufrufe sie verwenden können.

## Verhalten der RPCs

| RPC | Eingabe und Regeln | Ergebnis |
| --- | --- | --- |
| `CreateTrip` | Gespeichertes Profil mit Fahrzeug; neue `request_id` als UUID; vollständiger Start/Ziel getrimmt, verschieden und jeweils 1–500 Zeichen ohne Steuerzeichen; Abfahrt zukünftig, höchstens 366 Tage entfernt; 1–8 Plätze innerhalb Fahrzeugkapazität; positive manuelle `distance_km` | Neue Fahrt mit Status `PLANNED` und Kostenvorschau |
| `GetTrip` | Nicht leere UUID mit Bindestrichen | Fahrt mit Berechtigungsflags, eigener Anfrage und sichtbaren Buchungen; Route/Abholstopps nur für den Fahrer |
| `SearchTrips` | Pflichtzeitraum `[departure_from, departure_until)` mit höchstens 31 Tagen; optionale exakte, groß-/kleinschreibungsabhängige Ortsfilter; `offset` 0–10000; `page_size` 0 = Standard 20, sonst 1–100 | Nur geplante, noch nicht abgefahrene Fahrten; sortiert nach Abfahrt und ID; optionaler `next_offset` |
| `CancelTrip` | UUID; nur derselbe Eigentümer aus demselben Issuer; eine bereits abgefahrene Fahrt kann nicht erstmals abgesagt werden | Status `CANCELLED`; wiederholtes Absagen bleibt erfolgreich |
| `ListMyTrips` | Aktuelle Person, begrenzte Paginierung | Eigene Angebote einschließlich vergangener, abgesagter und abgeschlossener Fahrten |

Die weiteren Services verwenden dieselben Transport-, Fehler- und Identitätsregeln:

| Service | RPCs und Regeln |
| --- | --- |
| `ProfileService` | `GetMyProfile`, `UpdateMyProfile`; Name, vollständige Abholadresse und ein optionales Fahrzeug mit Antrieb, Verbrauch und Energiepreis |
| `BookingService` | `RequestSeat`, `AcceptBooking`, `RejectBooking`, `WithdrawBooking`, `ListMyBookings`; nur bestätigte Anfragen belegen Plätze; Rückzug bis Abfahrt; nach Ablehnung/Rückzug erneut anfragen |
| `RecurringTripService` | `CreateSeries`, `ListMySeries`, `CancelSeries`; ISO-Wochentage, Start-/Enddatum und feste Uhrzeit Europe/Berlin; mehrdeutige/nicht existierende DST-Zeiten werden abgelehnt |
| `BillingService` | `CompleteTrip`, `GetBill`, `ReportPayment`, `ConfirmPayment`, `RejectPayment`; Fahrer schließt nach Abfahrt ab, friert Kosten/Anteile ein und bestätigt gemeldete Zahlungen |

Entfernungen werden manuell eingegeben. Die Kostenformel lautet Strecke × Verbrauch / 100 × Energiepreis, gleichmäßig einschließlich Fahrer aufgeteilt. Verbrauch, Preis und Kilometer sind positive Dezimalstrings mit Punkt und höchstens vier Nachkommastellen; Geldbeträge werden als ganze `int64`-Centwerte übertragen. Es gibt keinen Online-Zahlungsvorgang. Der Google-Maps-Link zeigt vollständige Adressen und bestätigte Abholstopps, optimiert aber keine Route. Automatisches Routing und eine kostenlose API-Key-Integration werden später ergänzt.

Zeitpunkte werden in UTC gespeichert. PostgreSQL speichert Mikrosekunden; kleinere Sekundenbruchteile werden vor Schreiben und Vergleichen abgeschnitten. Offset-Paginierung bildet keinen unveränderlichen Snapshot: Änderungen zwischen Seiten können Ergebnisse verschieben. Für mehr als 10000 übersprungene Einträge den Suchzeitraum eingrenzen.

`CreateTrip` ist pro `(iss, sub, request_id)` idempotent: Bei einem Retry dieselbe UUID und dieselben normalisierten Eingaben senden. Dann kommt dieselbe Fahrt zurück, auch nach einer zwischenzeitlichen Absage. Dieselbe UUID mit anderen Eingaben wird abgelehnt. Ein Unique-Index sichert auch gleichzeitige Retries ab. Für eine weitere Fahrt eine neue UUID erzeugen.

| Fehler | gRPC-Status |
| --- | --- |
| Ungültige Eingabe oder fehlender Zeitpunkt | `INVALID_ARGUMENT` |
| Fahrt unbekannt | `NOT_FOUND` |
| Fehlende/ungültige Authentifizierung | `UNAUTHENTICATED` |
| Fehlende Identitätsclaims oder fremde Fahrt ändern | `PERMISSION_DENIED` |
| `request_id` mit anderem Inhalt wiederverwendet | `ALREADY_EXISTS` |
| Bereits abgefahrene Fahrt absagen | `FAILED_PRECONDITION` |
| Abbruch / überschrittene Deadline | `CANCELLED` / `DEADLINE_EXCEEDED` |
| Unerwarteter Serverfehler | `INTERNAL`, keine internen Details |

## Security-Entscheidungen

[`AuthenticationExtensions`](../apps/api/Security/AuthenticationExtensions.cs) trennt Demo-Simulation, Bearer-Authentifizierung und die optional aktivierte Browser-Anmeldung. Mit `DemoMode:Enabled=true` ist ausschließlich in der Umgebung `Staging` eine vordefinierte Personenauswahl erlaubt. `GET /demo/users` liefert den Katalog; `X-Carpoolr-Demo-User` muss die gültige ausgewählte ID enthalten. Fehlende/unbekannte Personen werden abgewiesen; es gibt keine Standardperson. Der Simulationsmodus wird in Development und Production beim Start abgelehnt und kann nicht gleichzeitig mit der Browser-Anmeldung aktiviert werden.

Ohne Demo-Modus werden signierte JWT-Access-Tokens mit Prüfung von Signatur, Issuer, Audience und Laufzeit verwendet. Mit `Authentication:Browser:Enabled=true` ist zusätzlich die serverseitige OIDC-Anmeldung mit HttpOnly-Cookie-Sitzung und CSRF-Schutz verfügbar. In dieser Authentik-Konfiguration muss ein Bearer-Access-Token zusätzlich die erwartete `azp`-Client-ID und einen nicht leeren `scope` enthalten; ein ID-Token allein autorisiert keinen RPC. Ein ungültiger Authorization-Header fällt nicht auf eine Cookie- oder Demo-Identität zurück.

Neue Endpunkte benötigen standardmäßig eine bestätigte Identität. `/healthz` und `/readyz` sind ausdrücklich öffentlich; der erste prüft nur den Prozess, der zweite die Datenbankbereitschaft einschließlich Migrationen. `/auth/session` liefert öffentlich die Betriebsart und den eigenen Anmeldestatus, keine fremden Benutzerdaten. Login-Start und OIDC-Callback müssen ebenfalls ohne bestehende App-Sitzung erreichbar sein.

Die Policy `Colleague` verlangt eine bestätigte Identität mit `iss` und `sub`. Den bestätigten Benutzerkreis legen die Freischaltungen an der Authentik-Application fest; eine zusätzliche lokale Unternehmensgruppe wurde nicht verlangt. Die konfigurierte API-Audience muss mit dem tatsächlichen Access-Token übereinstimmen. Fahrer-IDs werden nicht aus Requests übernommen. Eigentum verwendet beide Identitätsclaims, weil derselbe `sub` bei verschiedenen Issuern unterschiedliche Personen bezeichnet. Diese internen IDs stehen nicht in Fahrtantworten.

Abholadressen sind für die eigene Person und nach Zusage für den Fahrer sichtbar. Fremde Mitfahranfragen und private Routenstopps werden nicht an andere Betrachter ausgegeben. Im Testmodus kann bewusst zwischen den festen Rollen gewechselt werden; dies ist keine echte Anmeldung oder Zugriffstrennung zwischen den Testpersonen.

CORS erlaubt konfigurierte Origins und gibt gRPC-Statusheader frei; CORS ersetzt keine Anmeldung. Requests sind auf 64 KiB, Antworten auf 1 MiB beschränkt. SQL wird durch EF parametrisiert. Requests und Tokens werden nicht vollständig geloggt. Die API hat noch kein Rate Limiting; vor breiterem Betrieb passende Grenzen am Gateway oder an der API festlegen.

## Lokal die Staging-Anwendung testen

Für den vollständigen Browser-Test ohne Login PostgreSQL gemäß README starten. Alle Befehle laufen im Repository-Stamm. ASP.NET Core lädt `.env` nicht automatisch; in Staging werden auch Development-User-Secrets nicht automatisch geladen.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Staging'
$env:DemoMode__Enabled = 'true'
$env:ConnectionStrings__Default = 'Host=localhost;Port=5432;Database=carpoolr;Username=carpoolr;Password=change-me-local-only'
$env:Cors__AllowedOrigins__0 = 'http://localhost:5173'
dotnet run --project apps/api --no-launch-profile -- --migrate
dotnet run --project apps/api --no-launch-profile -- --urls http://localhost:5000
```

Die Connection String anpassen. `--migrate` führt die enthaltenen EF-Migrationen aus und beendet sich. Im Cluster erledigt das ein eigener Migrations-Job; normale API-Starts verändern das Schema nicht. Für den UI-Test in einem zweiten Terminal `bun run --cwd apps/web dev` starten und eine Person auswählen. Bei einer anderen API-Adresse `VITE_API_BASE_URL` vor dem Frontendstart setzen.

In Rider eine entsprechende Run-Konfiguration mit Staging-Umgebung und denselben Variablen verwenden. Die bestehenden Profile `http`/`https` setzen Development; sie daher nicht unverändert für den Staging-Demo-Modus verwenden. Im Cluster bleibt die Demo-Konfiguration auf das Staging-Overlay beschränkt.

## Lokal mit Rider und signierten Tokens entwickeln

`Carpoolr.slnx` öffnen. Alle folgenden Befehle laufen im Repository-Stamm. PostgreSQL gemäß README starten (`make db-up`). Das .NET-Programm lädt eine `.env` **nicht automatisch**: Werte als Prozessumgebung/Rider-Umgebungsvariablen oder als lokale User Secrets setzen.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet user-secrets set --project apps/api 'ConnectionStrings:Default' 'Host=localhost;Port=5432;Database=carpoolr;Username=carpoolr;Password=change-me-local-only'
dotnet ef database update --project apps/api
dotnet user-jwts create --project apps/api --name alice --valid-for 1h
dotnet dev-certs https --trust
dotnet run --project apps/api --launch-profile https
```

Die Connection String an deine lokale Datenbank anpassen. Für diese Variante `DemoMode__Enabled` entfernen bzw. auf `false` setzen. User Secrets werden nur in `Development` automatisch geladen; sie gehören nicht ins Repository. Migrationen werden bewusst separat ausgeführt, nicht bei jedem API-Start.

`user-jwts` erzeugt einen signierten lokalen Token, speichert den Schlüssel im Benutzerprofil und ergänzt lokale Issuer-/Audience-Einstellungen in `appsettings.Development.json`. Anschließend die API neu starten. Die Änderungen vor einem Commit prüfen; Token und Schlüssel nicht einchecken. Diese Tokens dienen ausschließlich der lokalen Entwicklung. [Microsoft: user-jwts](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn?view=aspnetcore-10.0)

In Rider das Profil `https` verwenden. `https://localhost:7001` unterstützt natives gRPC über HTTP/2. `http://localhost:5000` unterstützt gRPC-Web über HTTP/1.1; ein nativer gRPC-Client kann diesen HTTP/1.1-Port nicht verwenden. Zertifikatsprüfung beibehalten und das Entwicklungszertifikat vertrauen.

## Einen Aufruf in Rider ausprobieren

Die Rider-Plugins **Protocol Buffers** und **gRPC** aktivieren und [`TripService.http`](../apps/api/TripService.http) öffnen. Rider liest den Vertrag direkt; Server-Reflection ist dafür nicht erforderlich. `GRPC grpcs://...`, Metadaten und Skripte entsprechen der [Rider-Dokumentation](https://www.jetbrains.com/help/rider/Http_client_in__product__code_editor.html).

Neben der HTTP-Datei lokal `apps/api/http-client.private.env.json` anlegen; diese Datei ist gitignored:

```json
{
  "local": {
    "access_token": "DEIN_LOKALER_TOKEN",
    "trip_id": "ID_AUS_DER_CREATE_ANTWORT"
  }
}
```

In Rider die Umgebung `local` auswählen und zuerst **nur CreateTrip** ausführen. Das Skript erzeugt eine UUID und eine Abfahrt morgen; beide bleiben für Retries während derselben Rider-Sitzung erhalten. Für eine neue Fahrt den beschriebenen globalen Beispielwert einmal löschen. Die offiziellen [HTTP-Client-Variablen](https://www.jetbrains.com/help/rider/HTTP-Client-variables.html) erklären `$random.uuid` und den Skriptzugriff.

Vor `CreateTrip` ein Profil mit vollständiger Adresse und Fahrzeug über `ProfileService.UpdateMyProfile` speichern; der Aufruf steht ebenfalls in der Beispieldatei. Danach die im Beispiel markierten vollständigen Start-/Zieladressen und die manuelle Strecke anpassen. Die zurückgegebene `trip.id` als `trip_id` in die private Datei kopieren. `GetTrip`, `SearchTrips` und `CancelTrip` anschließend einzeln ausführen. Die Suche berechnet einen aktuellen Zeitraum automatisch. Zum Testen der Eigentümerprüfung einen zweiten lokalen Token erzeugen. Token und private Datei nicht committen.

## Browser und Cluster

Vue benötigt einen generierten **gRPC-Web**-Client; ein normaler JSON-`fetch` auf den RPC-Pfad genügt nicht. Lokal ist die Basisadresse `http://localhost:5000`. Der vorhandene nginx leitet `/api/` zur API weiter und entfernt dieses Präfix. Im Cluster verwendet der Browser deshalb dieselbe Website-Origin mit Basis `/api`, zum Beispiel `/api/carpoolr.trips.v1.TripService/GetTrip`. [Microsoft: gRPC-Web](https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb?view=aspnetcore-10.0)

Clients sollten eine passende Deadline setzen und Abbrüche weiterreichen. Bei einem Timeout kann eine Änderung trotzdem bereits gespeichert sein: `CreateTrip` deshalb nur mit unveränderter `request_id` wiederholen. Permanente Validierungs- und Berechtigungsfehler nicht automatisch erneut senden.

Eine native gRPC-Verbindung braucht eine gesonderte HTTP/2-fähige Route; diese ist im Cluster noch nicht eingerichtet. Auch TLS am öffentlichen Ingress ist vor dem Betrieb zu konfigurieren. Beim Transport nach TLS-Terminierung muss die Verbindung im Cluster passend abgesichert sein.

Mit ausgeschaltetem Demo-Modus verlangt die API außerhalb von `Development` beim Start `Authentication__Authority` als HTTPS-Adresse des tatsächlichen Authentik-Issuers und `Authentication__Audience` als konfigurierte API-Audience. Außerdem `ConnectionStrings__Default` und bei getrennten Origins `Cors__AllowedOrigins__0` setzen. Signatur-/Issuer-/Audience-Prüfungen bleiben aktiv. Vue unterstützt jetzt die ausdrücklich aktivierte serverseitige Authentik-Anmeldung unter derselben HTTPS-Origin mit `/api`; Einrichtung, Cookie-/CSRF-Schutz und persistente Schlüssel beschreibt [Authentik anbinden](authentication.md). Der tatsächliche Provider ist noch nicht erstellt, daher bleibt das beobachtete Staging zunächst im Demo-Modus.

## Integrationstests

Die Tests verwenden den echten gRPC-Web-/JWT-/EF-Pfad sowie den Staging-Demo-Modus und benötigen eine **eigene Testdatenbank**. Sie führen Migrationen aus und schreiben Testdaten; niemals eine produktive, Staging- oder persönlich genutzte Datenbank angeben. Beispiel für einen kurzlebigen PostgreSQL-Container:

```powershell
docker run --detach --rm --name carpoolr-grpc-tests --publish 127.0.0.1:55432:5432 --env POSTGRES_PASSWORD=local-tests-only --env POSTGRES_DB=carpoolr_tests postgres:17
dotnet test tests/Carpoolr.Api.Tests --configuration Release
docker stop carpoolr-grpc-tests
```

Vor dem Testlauf warten, bis PostgreSQL Verbindungen akzeptiert. Standard ist Port 55432, Datenbank `carpoolr_tests`, Benutzer `postgres`, Passwort `local-tests-only`. Alternativ die vollständige Connection String über `CARPOOLR_TEST_POSTGRES` setzen. Der Testcontainer wird beim Stoppen entfernt; die Tests selbst löschen keine fremden Datenbanken.
