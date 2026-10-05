#!/bin/sh
# Task 33 (specs/integration/camera-stream.md): record the deployment's camera
# mode as a same-origin capability document the browser reads at startup
# (DroneService.getCameraMode -> GET /camera-mode.json).
#
# The gate is the same CAMERA_PROXY_TARGET that Task 32's 50-camera-location.sh
# uses for the /camera/ location, so "media plane enabled" and "real stream
# mode" can never disagree within the same container start. The file is a tiny
# static JSON under the SPA root (served by nginx) and contains no secret, no
# device path, and no IP.
#
# Runs via the official nginx image's /docker-entrypoint.d/*.sh hook (sourced
# with `sh`, so no exec bit required).
set -eu

TARGET="${CAMERA_PROXY_TARGET:-}"
MODE_FILE="${CAMERA_MODE_FILE:-/usr/share/nginx/html/camera-mode.json}"

if [ -n "$TARGET" ]; then
  printf '%s\n' '{"mode":"ustreamer"}' > "$MODE_FILE"
else
  printf '%s\n' '{"mode":"mock"}' > "$MODE_FILE"
fi