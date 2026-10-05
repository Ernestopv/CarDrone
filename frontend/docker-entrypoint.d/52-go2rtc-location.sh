#!/bin/sh
# WebRTC signaling passthrough: materialize the nginx /go2rtc/ location from
# GO2RTC_API_TARGET at container start (same Task 32 pattern as the camera one).
#
#   GO2RTC_API_TARGET empty/unset -> HTTP 404 (no go2rtc in this deployment,
#                                    e.g. PC mode; never the SPA fallback)
#   GO2RTC_API_TARGET set         -> proxy to the go2rtc API (same-origin SDP
#                                    signaling; the media itself is WebRTC).
#
# STATIC upstream (not a variable + resolver): the target is host.docker.internal
# (from extra_hosts), which nginx resolves from /etc/hosts at startup — Docker's
# embedded resolver (127.0.0.11) does not serve it. A literal proxy_pass with a
# UI ('/') also strips the /go2rtc/ prefix the normal way. It is only set when
# the camera profile (and thus go2rtc) is enabled, so the resolution never fails.
#
# Runs via the official nginx image's /docker-entrypoint.d/*.sh hook.
set -eu

LOCATIONS_DIR="${LOCATIONS_DIR:-/etc/nginx/locations}"
LOCATIONS_CONF="${GO2RTC_LOCATIONS_CONF:-go2rtc.conf}"

if [ -n "${GO2RTC_API_TARGET:-}" ]; then
  cat > "${LOCATIONS_DIR}/${LOCATIONS_CONF}" <<EOF
location /go2rtc/ {
    proxy_pass ${GO2RTC_API_TARGET}/;
    proxy_set_header Host \$host;
    proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto \$scheme;
}
EOF
else
  cat > "${LOCATIONS_DIR}/${LOCATIONS_CONF}" <<EOF
location /go2rtc/ {
    return 404;
}
EOF
fi
