# Build
FROM oven/bun:1.4.1@sha256:9e123d5fc069e29d519fd4c981afb61b8542ac80274771961136db1e4538d53e AS build
WORKDIR /workspace/apps/web

COPY apps/web/package.json apps/web/bun.lock ./
RUN bun install --frozen-lockfile

COPY apps/web/ ./
COPY apps/api/Protos/ /workspace/apps/api/Protos/

ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN bun run proto:check && bun run build

# Runtime
FROM nginx:1.28-alpine

COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /workspace/apps/web/dist/ /usr/share/nginx/html/

EXPOSE 80
