
# Carpoolr

Private Mitfahr-App für ein kleines Team. (Private carpooling app — simple English below.)

The staging application supports preset test-person selection without login, profiles with one optional car, ride offers and approval-based bookings, weekly recurring trips, equal EUR cost sharing, and payment reporting with driver confirmation. Start with the [application guide](docs/frontend-application.md); the [confirmed scope](docs/application-scope.md) records the product decisions.

Distances are entered manually. The Google Maps link includes addresses and accepted pickup stops, but does **not** optimize the route. Automatic routing is deferred until a suitable free service and API key are provided; no paid Google routing API is required by this implementation.

## Stack

| Teil        | Technologie                                        |
| ----------- | -------------------------------------------------- |
| Frontend    | Vue 3 + TypeScript (Vite), Pakete/Scripts mit Bun  |
| Backend API | ASP.NET Core (.NET 10, C#), gRPC + gRPC-Web und Health-Controller |
| Datenbank   | PostgreSQL 17 (Docker Compose, named volume)      |

## Prerequisites

- [Bun](https://bun.sh) ≥ 1.4 (`bun --version`)
- .NET SDK 10 (`dotnet --version` → 10.x)
- Docker Desktop running with Linux containers and Docker Compose
- GNU Make for the shortcuts below (after installation, open a new terminal so PATH is refreshed)

## Setup

```bash
cp .env.example .env   # adjust values if you like (gitignored)
```

In Windows PowerShell, use `Copy-Item .env.example .env` instead.

Docker Compose reads the root `.env`. ASP.NET Core reads **process environment variables and local User Secrets**; it does not load `.env` automatically. See the [gRPC TripService guide](docs/grpc-trips.md) for API setup, authentication, migrations, and Rider examples.

| Variable                      | Used by  | Meaning                                              |
| ----------------------------- | -------- | ---------------------------------------------------- |
| `POSTGRES_USER/PASSWORD/DB`   | compose  | PostgreSQL credentials (fail fast if missing)        |
| `POSTGRES_PORT`               | compose  | Host port (default 5432 — override if it is taken)   |
| `ConnectionStrings__Default` | API      | .NET connection string (`__` = config hierarchy)     |
| `VITE_API_BASE_URL`           | Web      | API base URL for gRPC-Web and health fetch (default :5000) |
| `DemoMode__Enabled`           | API      | Explicit preset-person simulation, permitted only in `Staging` |
| `Cors__AllowedOrigins__0`     | API      | Allowed frontend origin when the browser calls a different API origin |
| `Authentication__Authority` / `Authentication__Audience` | API | Trusted HTTPS issuer and API audience when demo mode is disabled outside Development |

For local testing of the complete application **without login**, use the Staging/demo configuration in the [API guide](docs/grpc-trips.md#lokal-die-staging-anwendung-testen). The default Rider launch profiles use Development and signed local JWTs; they do not enable the staging simulation.

## Dev commands

```bash
make db-up      # PostgreSQL 17 on :5432 (waits until healthy)
make dev-api    # BFF on http://localhost:5000  (dotnet run --project apps/api)
make dev-web    # Vue dev server on http://localhost:5173  (bun run in apps/web)
make db-down    # stop PostgreSQL (volume `carpoolr_pgdata` is kept)
```

The frontend calls the API using **binary gRPC-Web + CORS**; `/healthz` remains a normal HTTP request. There is deliberately no Vite dev proxy. The deployed nginx forwards `/api/` to the API and removes that prefix.

## Checks

```bash
# in apps/web
bun install --frozen-lockfile
bun run proto:check          # generated TypeScript matches the local protobuf contract
bun run format:check         # authored frontend source uses the pinned Prettier configuration
bun run typecheck            # vue-tsc --noEmit
bun run build                # typecheck + vite build → dist/
bun run test                 # hermetic unit tests (vitest)
bun run test:integration     # calls api/client.ts health() against the LIVE API

# in apps/api / repo root
dotnet build apps/api        # 0 warnings, 0 errors
dotnet test tests/Carpoolr.Api.Tests --configuration Release
# Backend tests require a dedicated PostgreSQL database; see docs/grpc-trips.md.
```

## Notes & known caveats

- **`test:integration` is not hermetic:** start the API first (`make dev-api`), then run it. It exercises the real `src/api/client.ts` including `VITE_API_BASE_URL` resolution.
- **The application uses PostgreSQL:** configure `ConnectionStrings:Default` using User Secrets or process environment variables. Apply migrations separately with `dotnet run --project apps/api --no-launch-profile -- --migrate` under the intended environment and credentials. The process exits after migrating; normal API replicas do not migrate on startup. A dedicated staging Job uses this mode.
- **Health endpoints:** `/healthz` is anonymous liveness without a database query; `/readyz` checks database availability and the current migration state.
- **Staging has an explicit no-login simulation.** Set `ASPNETCORE_ENVIRONMENT=Staging` and `DemoMode__Enabled=true`; the UI loads the preset-person catalog and sends the chosen identity. This mode is rejected in Development and Production. With demo mode disabled, RPCs require signed access tokens. Outside Development configure `Authentication:Authority` (HTTPS) and `Authentication:Audience`. See [the API guide](docs/grpc-trips.md).
- **Docker Desktop must be running** with Linux containers. `docker compose up -d --wait` waits for the database healthcheck.
- **`db/initdb/` scripts run only on FIRST init** of the empty volume. Once `carpoolr_pgdata` exists, changes there have no effect — use the included EF Core migrations instead.
- **Port conflicts (:5432):** set `POSTGRES_PORT` in `.env` — and also update `Port=` in `ConnectionStrings__Default` in the same file, or the API will later connect to the old port.
- **Security:** `.env` is gitignored. The default Postgres password is for local development only — never reuse it elsewhere. Dev CORS is restricted to the two Vite origins (`http://localhost:5173`, `http://127.0.0.1:5173`), not `*`.
- **Recurring rides:** weekly ISO weekdays with inclusive start/end dates and a fixed Europe/Berlin time are implemented. Individual future occurrences and all future planned occurrences of a series can be cancelled. Ambiguous/nonexistent daylight-saving times are rejected.
- **Frontend transport and code generation:** see [frontend-grpc.md](docs/frontend-grpc.md). The [application guide](docs/frontend-application.md) explains profiles, requests, costs, and the payment workflow.
- **Backend tests require isolated PostgreSQL:** they apply migrations and create test records. Use a dedicated test database via `CARPOOLR_TEST_POSTGRES`; never point them at staging or production data. See [test setup](docs/grpc-trips.md#integrationstests).

## Repo layout

```
apps/
  web/    Vue 3 + TS frontend (Vite, Bun)
  api/    ASP.NET Core API (.NET 10, gRPC-Web + health controller)
tests/    Backend integration tests with isolated PostgreSQL, JWT and staging-person modes
docs/     Confirmed scope, API architecture, frontend and staging guides
db/       docker-compose.yml (postgres:17) + initdb/ baseline
Makefile  db-up / db-down / dev-api / dev-web shortcuts
```

