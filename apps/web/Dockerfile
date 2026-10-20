# Build
FROM oven/bun@sha256:789674f246b61a3b02edec72a4cd0ebdfaddffab4b24f6e9147dde2937325897 AS build
WORKDIR /app

COPY apps/web/package.json apps/web/bun.lock ./
RUN bun install --frozen-lockfile

COPY apps/web/ ./

ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN bun run build

# Runtime
FROM nginx:1.28-alpine

COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/ /usr/share/nginx/html/

EXPOSE 80