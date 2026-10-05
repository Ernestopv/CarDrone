#!/bin/sh
# uStreamer container entry point (Task 32; specs/docker/ustreamer-container.md).
#
# Maps operator-tunable CAMERA_* environment to uStreamer CLI options. The
# format defaults to MJPEG and the defaults are the Task 31 encoder design
# defaults (1280x720 / 15 fps / JPEG quality 80); whether the real camera/Pi
# supports them is NOT VERIFIED until Task 36. MJPEG is the inventory-
# recommended native format for the attached camera (lower USB bandwidth);
# YUYV at 1280x720 caused device select() timeouts on the target.
# The device default (/dev/video0) is uStreamer's own documented default — the
# actual device mapping is provided later from the Task 24 inventory evidence.
set -eu

exec /usr/local/bin/ustreamer \
    --host 0.0.0.0 \
    --port "8080" \
    --device "${CAMERA_DEVICE:-/dev/video0}" \
    --format "${CAMERA_FORMAT:-MJPEG}" \
    --resolution "${CAMERA_RESOLUTION:-1280x720}" \
    --desired-fps "${CAMERA_FPS:-15}" \
    --quality "${CAMERA_QUALITY:-80}"