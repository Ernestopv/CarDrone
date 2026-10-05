# Stage 1: build the React app. The package manifests are copied before the
# source so the npm ci layer stays cached across code edits (Docker skill
# pattern; spec: specs/docker/full-stack-compose.md).
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Empty base URL = same origin: the built app fetches relative /api/... paths
# and nginx proxies them to the backend service, so no host name or port is
# ever baked into the image (spec decision 1).
ARG VITE_API_BASE_URL=
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# Stage 2: serve the static bundle with nginx. nginx:1-alpine ships busybox
# wget, which the compose healthcheck uses — the Dockerfile itself carries no
# HEALTHCHECK (orchestrator-level probes only, Task 13 convention).
FROM nginx:1-alpine AS runtime

RUN mkdir -p /etc/nginx/locations

# Task 32 (specs/docker/ustreamer-container.md): the entry point renders the
# /camera/ nginx location from CAMERA_PROXY_TARGET at container start.
COPY docker-entrypoint.d/ /docker-entrypoint.d/

# The official nginx entrypoint only runs executable *.sh files; ensure the bit
# regardless of the host filesystem (Windows checkouts, tar without modes...)
# so the /camera/ location is always materialized before nginx reads its config.
RUN chmod 0755 /docker-entrypoint.d/*.sh

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
