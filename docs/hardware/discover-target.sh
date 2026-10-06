#!/bin/sh
# CarDrone target discovery probe (docs/hardware/raspberry-pi-inventory.md).
#
# READ-ONLY enumeration for the REAL Raspberry Pi target. Prints host facts to
# stdout so the output can be attached to the inventory as evidence. It never
# writes (no export, no sysfs writes, no device opens beyond metadata), never
# uses privileged mode, and never guesses values. Run either on the host:
#
#   sh docs/hardware/discover-target.sh
#
# or from a least-privilege, read-only container on the Pi:
#
#   docker run --rm \
#     -v /sys:/sys:ro \
#     -v /dev:/dev:ro \
#     -v "$PWD":/probe:ro \
#     debian:bookworm-slim sh /probe/docs/hardware/discover-target.sh
#
# A container sees the same kernel/sysfs/dev of the Pi and can enumerate the
# same facts; it cannot fill the operator-supplied configuration by itself.
set -u

say() { printf '%s\n' "## $*"; }

say "os / kernel / arch"
date -Is
uname -a
uname -m
cat /etc/os-release 2>/dev/null || true

say "container vs host (expect 'container' when run inside Docker)"
cat /proc/self/cgroup 2>/dev/null | cut -d: -f3- | head -n1 || true

say "gpio character devices and memory node (stat only, never opened)"
for node in /dev/gpiochip* /dev/gpiemom; do
  [ -e "$node" ] && stat -c '%n mode=%a uid=%u gid=%g owner=%U group=%G' "$node" 2>/dev/null || true
done

say "groups available (look for gpio / dialout / video)"
getent group 2>/dev/null | grep -E 'gpio|dialout|video' || true
id -un
id -Gn

say "pwm sysfs chips"
for chip in /sys/class/pwm/pwmchip*; do
  [ -e "$chip" ] || continue
  printf '%s\n' "chip=$chip"
  [ -r "$chip/uevent" ] && cat "$chip/uevent" 2>/dev/null || true
  [ -r "$chip/npwm" ] && printf 'npwm=%s\n' "$(cat "$chip/npwm" 2>/dev/null)" || true
  for out in "$chip"/pwm*; do
    [ -d "$out" ] || continue
    printf '  output=%s\n' "$(basename "$out")"
    [ -r "$out/period" ] && printf '    period=%s\n' "$(cat "$out/period" 2>/dev/null)" || true
    [ -r "$out/duty_cycle" ] && printf '    duty_cycle=%s\n' "$(cat "$out/duty_cycle" 2>/dev/null)" || true
    [ -r "$out/enable" ] && printf '    enable=%s\n' "$(cat "$out/enable" 2>/dev/null)" || true
  done
done

say "pwm device-tree facts that operators must confirm (NOT VERIFIED by this probe)"
if [ -r /boot/firmware/config.txt ]; then
  grep -n -iE '^\s*dtoverlay=.*pwm|pinctrl' /boot/firmware/config.txt 2>/dev/null || printf 'no PWM dtoverlay line found in /boot/firmware/config.txt\n'
fi
if [ -r /boot/config.txt ]; then
  grep -n -iE '^\s*dtoverlay=.*pwm|pinctrl' /boot/config.txt 2>/dev/null || printf 'no PWM dtoverlay line found in /boot/config.txt\n'
fi

say "docker engine"
docker version --format 'client={{.Client.Version}} server={{.Server.Version}}' 2>/dev/null || true
docker compose version 2>/dev/null || true

say "i2c bus nodes (stat only, never opened)"
for node in /dev/i2c*; do
  [ -e "$node" ] && stat -c '%n mode=%a uid=%u gid=%g owner=%U group=%G' "$node" 2>/dev/null || true
done
getent group 2>/dev/null | grep -E '^\s*i2c\b' || true

say "ina219 address - NOT VERIFIED by this probe"
# The probe never opens a device, so the INA219 7-bit address cannot be read
# here. It is recorded by the operator (e.g. the read-only register probe in
# specs/hardware/battery-monitoring.md run as root) into the inventory.

say "camera v4l2 nodes (metadata only)"
for node in /dev/video*; do
  [ -e "$node" ] && stat -c '%n mode=%a uid=%u gid=%g group=%G' "$node" 2>/dev/null || true
done

say "end of probe - attach this output to docs/hardware/raspberry-pi-inventory.md"