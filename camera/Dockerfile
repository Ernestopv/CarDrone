# uStreamer container image (Task 32; specs/docker/ustreamer-container.md).
#
# Multi-stage build of uStreamer (https://github.com/pikvm/ustreamer) pinned
# to a specific release tag — never 'latest' (the tag is recorded in the spec's
# prerequisites ledger and docs/DOCKER.md). Built for the Raspberry Pi
# (linux/arm64); the tag is the latest upstream tag as of this task.
#
# Build dependencies and default port/endpoint facts are taken from the
# pinned uStreamer documentation (README/man), not invented:
#   - Debian bookworm build deps: build-essential, pkg-config, libevent-dev,
#     libbsd-dev, libjpeg62-turbo-dev
#   - default HTTP port 8080, default MJPEG endpoint /stream,
#     default device /dev/video0

# Hadolint suggestion: ARG defaults are visible and overridable.
ARG USTREAMER_TAG=v6.67

FROM debian:bookworm-slim AS build

ARG USTREAMER_TAG

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates git make gcc pkg-config \
        libevent-dev libbsd-dev libjpeg62-turbo-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt
RUN git clone --depth 1 --branch "${USTREAMER_TAG}" \
        https://github.com/pikvm/ustreamer.git ustreamer-src

WORKDIR /opt/ustreamer-src
RUN make -j"$(nproc)"

# Minimal runtime stage: non-root, runtime libraries only.
FROM debian:bookworm-slim AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libevent-2.1-7 libevent-core-2.1-7 libevent-extra-2.1-7 \
        libevent-pthreads-2.1-7 libevent-openssl-2.1-7 \
        libbsd0 libjpeg62-turbo \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system ustreamer \
    && useradd --system --gid ustreamer --create-home --home-dir /home/ustreamer ustreamer

COPY --from=build /opt/ustreamer-src/ustreamer /usr/local/bin/ustreamer
COPY entrypoint.sh /usr/local/bin/ustreamer-entrypoint.sh
RUN chmod 0755 /usr/local/bin/ustreamer-entrypoint.sh

USER ustreamer
WORKDIR /home/ustreamer

# Internal listen port only — never published on a host (media plane reached
# via the frontend nginx /camera/ passthrough; CAMERA_PROXY_TARGET).
EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/ustreamer-entrypoint.sh"]