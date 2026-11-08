# Carpoolr — local dev shortcuts
# Container runtime: Docker Desktop (Linux containers).

ROOT_ENV := .env
COMPOSE := docker compose --env-file $(ROOT_ENV) -f db/docker-compose.yml

.PHONY: db-up db-down db-logs db-ps dev-api dev-web

## db-up: start PostgreSQL 17, wait until healthy
db-up:
	$(COMPOSE) up -d --wait

## db-down: stop and remove the PostgreSQL container (volume is kept)
db-down:
	$(COMPOSE) down

## db-logs: follow PostgreSQL logs
db-logs:
	$(COMPOSE) logs -f

## db-ps: show container status
db-ps:
	$(COMPOSE) ps

## dev-api: run the .NET BFF on http://localhost:5000
dev-api:
	dotnet run --project apps/api

## dev-web: run the Vue dev server on http://localhost:5173
dev-web:
	bun run --cwd apps/web dev
