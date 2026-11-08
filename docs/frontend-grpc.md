# Typisierter gRPC-Web-Client für Vue

Das Frontend verwendet die bestehenden Verträge aus `apps/api/Protos`. [`grpc.ts`](../apps/web/src/api/grpc.ts) erzeugt mit `createGrpcWebTransport` einen binären **gRPC-Web**-Client für ASP.NET Core. Es werden keine Connect-Protokoll-Endpunkte vorausgesetzt. Die bestehenden Vue-/Vite-Struktur und der HTTP-Health-Client bleiben erhalten.

Die vollständige Anwendung und ihre explizite Testpersonenauswahl sind in [Frontend-Anwendung](frontend-application.md) beschrieben. Die dortige `application.ts` verbindet alle fünf generierten Services; die hier dokumentierte Transport-Factory bleibt unabhängig von dieser Personenauswahl.

## Lokale Codegenerierung

Im Repository-Stamm:

```powershell
bun install --cwd apps/web --frozen-lockfile
bun run --cwd apps/web proto:generate
bun run --cwd apps/web proto:check
```

`proto:generate` aktualisiert `apps/web/src/api/generated`. Diese Dateien werden eingecheckt und nicht von Hand bearbeitet. Der Generator liest über [`buf.gen.yaml`](../apps/web/buf.gen.yaml) ausschließlich lokale `.proto`-Dateien und läuft lokal über Bun; kein BSR-Konto, kein Schema-Upload und keine global installierten Buf-/protoc-Tools sind nötig. Die npm-Pakete werden beim Installieren heruntergeladen.

`proto:check` erzeugt dieselben Dateien in einem temporären Verzeichnis, vergleicht Inhalt und Dateiliste und beendet sich bei Abweichungen mit Fehlercode 1. Der Check verändert keine eingecheckten Dateien und eignet sich für CI. Nach einer Vertragsänderung `proto:generate` ausführen und Vertrag sowie erzeugte Dateien gemeinsam reviewen. Windows-CRLF allein gilt nicht als Änderung.

Runtime und Generator sind exakt gepinnt: `@connectrpc/connect` / `connect-web` 2.3.0, `@bufbuild/protobuf` / `protoc-gen-es` 2.17.0 und `@bufbuild/buf` 1.75.0. Der Generator wird ausdrücklich mit Bun ausgeführt, sodass sein Node-Shebang keine globale Node-Installation voraussetzt. Die Codegenerierung wurde mit Bun 1.4.4 geprüft.

Der Web-Docker-Build kopiert zusätzlich `apps/api/Protos` und führt vor dem Build `proto:check` aus. Der Build-Kontext bleibt der Repository-Stamm.

## Client verwenden

Eine Clientinstanz wiederverwenden. Standardbasis ist `VITE_API_BASE_URL`, sonst `http://localhost:5000`. Im bestehenden Cluster-Proxy ist `/api` die passende Basis. Der Client ergänzt selbst den Service-/Methodenpfad.

```typescript
import { timestampFromDate } from '@bufbuild/protobuf/wkt'
import { createTripClient } from './api/grpc'

const trips = createTripClient()
const controller = new AbortController()
const from = new Date()
const until = new Date(from.getTime() + 24 * 60 * 60 * 1000)

const result = await trips.searchTrips({
  departureFrom: timestampFromDate(from),
  departureUntil: timestampFromDate(until),
  pageSize: 20,
}, {
  signal: controller.signal,
  timeoutMs: 5_000,
})

// controller.abort() beispielsweise beim Verlassen der Ansicht aufrufen.
```

Protobuf-Zeitpunkte bleiben typisierte `Timestamp`-Nachrichten; `timestampFromDate` und `timestampDate` aus `@bufbuild/protobuf/wkt` übernehmen die Konvertierung. Eingabevalidierung und Berechtigungen bleiben Aufgabe des Servers. Beim Erstellen einer Fahrt eine `requestId` einmal erzeugen und bei Retries unverändert mit denselben Daten verwenden.

## Metadaten und mögliche spätere Authentifizierung

Die Factory implementiert weder Anmeldung noch Benutzerwahl, Sitzung, Tokenablage oder Beispielidentitäten. Ohne Provider wird kein Authorization-Header hinzugefügt. Der Server bestimmt, welche Identität und Berechtigung ein Aufruf benötigt.

Optionale Integrationsstellen:

```typescript
const trips = createTripClient({
  baseUrl: '/api',
  defaultTimeoutMs: 10_000,
  // Bei jedem Aufruf erneut ausgewertet; vorhandene Integrationen hier anschließen.
  getAccessToken: async (signal) => tokenProvider(signal),
  getHeaders: async (signal) => metadataProvider(signal),
})
```

`tokenProvider` und `metadataProvider` stehen hier für später vom Aufrufer bereitgestellte Funktionen, nicht für vorhandene App-Funktionen. Beide erhalten das aktuelle Abbruchsignal. Explizite `headers` in den CallOptions haben Vorrang vor Provider-Werten; ein Token füllt nur einen noch nicht gesetzten Authorization-Header. Kein Token wird im Client gecacht. Bei Abbruch oder Deadline endet auch das Warten auf einen noch offenen Provider.

Jeder RPC akzeptiert die normalen `CallOptions`: `signal`, `timeoutMs`, `headers` und Header-/Trailer-Callbacks. Ohne `timeoutMs` bzw. `defaultTimeoutMs` setzt die Factory keine eigene Deadline. Fehler bleiben `ConnectError` mit den standardisierten `Code`-Werten; es gibt keine eigene Fehlercode- oder UX-Übersetzung. Beispielsweise `Code.NotFound`, `Code.Canceled` und `Code.DeadlineExceeded`. Tokens und sensible Header nicht loggen oder einchecken.

## Prüfungen

```powershell
bun run --cwd apps/web proto:check
bun run --cwd apps/web test
bun run --cwd apps/web build
```

Die neuen Tests prüfen echte binäre Request-/Response-Frames mit einem lokalen Fetch-Testdouble, Proxy-Pfade, wechselnde Metadaten, gRPC-Fehlertrailer, Abbruch und Deadlines. Dafür wird keine API gestartet. Ein durchgängiger Browser-/Staging-Test ist zusätzlich erforderlich, sobald Serverzugriff und UI-Abläufe feststehen.

Referenzen: [Connect: Client und Protokollwahl](https://connectrpc.com/docs/web/getting-started/), [Connect: Cancellation und Timeouts](https://connectrpc.com/docs/web/cancellation-and-timeouts/), [Buf: lokale Generator-Konfiguration](https://buf.build/docs/configuration/v2/buf-gen-yaml/).
