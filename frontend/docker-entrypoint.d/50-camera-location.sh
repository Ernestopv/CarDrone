#!/bin/sh
# Task 32 (specs/docker/ustreamer-container.md): materialize the nginx
# /camera/ location from CAMERA_PROXY_TARGET at container start.
#
#   CAMERA_PROXY_TARGET empty/unset -> HTTP 404 (nothing served; never the
#                                      SPA fallback so no client mistakes HTML
#                                      for a stream)
#   CAMERA_PROXY_TARGET set        -> byte-for-byte passthrough to the
#                                      compose-internal MJPEG URL (media plane;
#                                      control plane /api/ untouched).
#
# Runs via the official nginx image's /docker-entrypoint.d/*.sh hook (sourced
# with `sh`, so no exec bit required).
set -eu

LOCATIONS_DIR="${LOCATIONS_DIR:-/etc/nginx/locations}"
LOCATIONS_CONF="${LOCATIONS_CONF:-camera.conf}"

if [ -n "${CAMERA_PROXY_TARGET:-}" ]; then
  cat > "${LOCATIONS_DIR}/${LOCATIONS_CONF}" <<EOF
location /camera/ {
    # Lazy resolution: nginx must start even when uStreamer is down/absent, so
    # a camera failure surfaces as 502/504 at request time (never a dead nginx).
    resolver 127.0.0.11 valid=10s ipv6=off;
    set \$cardrone_camera_target "${CAMERA_PROXY_TARGET}";
    proxy_pass \$cardrone_camera_target;
    proxy_set_header Host \$host;
    postpone_output 0;
    proxy_buffering off;
    proxy_ignore_headers X-Accel-Buffering;
    proxy_read_timeout 60s;
}
EOF
else
  cat > "${LOCATIONS_DIR}/${LOCATIONS_CONF}" <<EOF
location /camera/ {
    return 404;
}
EOF
fi