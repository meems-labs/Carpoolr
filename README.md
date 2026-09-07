
# Carpoolr

Private Mitfahr-App für ein kleines Team. (Private carpooling app — simple English below.)

## Stack

| Teil        | Technologie                                        |
| ----------- | -------------------------------------------------- |
| Frontend    | Vue 3 + TypeScript (Vite), Pakete/Scripts mit Bun  |
| Backend BFF | ASP.NET Core minimal API (.NET 10, C#)             |
| Datenbank   | PostgreSQL 17 (rootless podman, named volume)      |

## Prerequisites

- [Bun](https://bun.sh) ≥ 1.4 (`bun --version`)
- .NET SDK 10 (`dotnet --version` → 10.x)
- podman (this machine uses podman rootless; `docker` is an alias for podman — Docker Desktop is not used)

## Setup

```bash
cp .env.example .env   # adjust values if you like (gitignored)
```

All services read configuration from the **single root `.env`**:

| Variable                      | Used by  | Meaning                                              |
| ----------------------------- | -------- | ---------------------------------------------------- |
| `POSTGRES_USER/PASSWORD/DB`   | compose  | PostgreSQL credentials (fail fast if missing)        |
| `POSTGRES_PORT`               | compose  | Host port (default 5432 — override if it is taken)   |
| `ConnectionStrings__Default` | API      | .NET connection string (`__` = config hierarchy)     |
| `VITE_API_BASE_URL`           | Web      | Base URL of the BFF for direct fetch (default :5000) |

## Dev commands

```bash
make db-up      # PostgreSQL 17 on :5432 (waits until healthy)
make dev-api    # BFF on http://localhost:5000  (dotnet run --project apps/api)
make dev-web    # Vue dev server on http://localhost:5173  (bun run in apps/web)
make db-down    # stop PostgreSQL (volume `carpoolr_pgdata` is kept)
```

The frontend calls the BFF **directly via fetch + CORS** — there is deliberately no Vite dev proxy, so dev and prod resolve `VITE_API_BASE_URL` identically.

## Checks

```bash
# in apps/web
bun install
bun run typecheck            # vue-tsc --noEmit
bun run build                # typecheck + vite build → dist/
bun run test                 # hermetic unit tests (vitest)
bun run test:integration     # calls api/client.ts health() against the LIVE API

# in apps/api / repo root
dotnet build apps/api        # 0 warnings, 0 errors
```

## Notes & known caveats

- **`test:integration` is not hermetic:** start the API first (`make dev-api`), then run it. It exercises the real `src/api/client.ts` including `VITE_API_BASE_URL` resolution.
- **The connection string is plumbed but unused this pass** — the API does not touch the database yet, so `ConnectionStrings__Default` being read from the environment is not covered by a test. The first DB access arrives with the data-model pass.
- **podman compose is podman-compose 1.6.0** (Python provider), not Docker Compose v2. `up -d --wait` is used to wait for the healthcheck; `start_interval` is not supported.
- **`db/initdb/` scripts run only on FIRST init** of the empty volume. Once `carpoolr_pgdata` exists, changes there have no effect — use migrations instead (added in a later pass).
- **Port conflicts (rootless podman on :5432):** set `POSTGRES_PORT` in `.env` — and also update `Port=` in `ConnectionStrings__Default` in the same file, or the API will later connect to the old port.
- **Security:** `.env` is gitignored. The default Postgres password is for local development only — never reuse it elsewhere. Dev CORS is restricted to the two Vite origins (`http://localhost:5173`, `http://127.0.0.1:5173`), not `*`.
- **Recurring rides:** intentionally not implemented yet. The stack is prepared so RFC 5545 RRULE-based recurrence (BYDAY, INTERVAL, UNTIL, COUNT) can be added cleanly in a later pass.

## Repo layout

```
apps/
  web/    Vue 3 + TS frontend (Vite, Bun)
  api/    ASP.NET Core BFF (.NET 10 minimal API)
db/       docker-compose.yml (postgres:17) + initdb/ baseline
Makefile  db-up / db-down / dev-api / dev-web shortcuts
```

