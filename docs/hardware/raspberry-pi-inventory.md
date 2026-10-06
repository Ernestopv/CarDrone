# Raspberry Pi Hardware Inventory

## Target

Model:

```text
Raspberry Pi 4 Model B Rev 1.1
```

Operating system / kernel / arch (probe evidence):

```text
Ubuntu 22.04.5 LTS
Linux 5.15.0-1108-raspi #111-Ubuntu SMP PREEMPT
aarch64 (arm64)
Docker Engine server 28.1.1
```

Note: the target runs **Ubuntu 22.04**, not Raspberry Pi OS/Debian. Containers
(`linux/arm64`) run against the 5.15 Raspberry Pi kernel; the repository makes
no Raspberry-Pi-OS-specific assumption.

## Camera

Detected device:

```text
FHD Camera Microphone: FHD Came
```

Driver:

```text
uvcvideo
```

Detected V4L2 nodes:

```text
/dev/video0
/dev/video1
```

Other `/dev/video*` nodes detected on the host belong to the Raspberry Pi codec/ISP stack and are not the selected USB camera capture device.

### Selected capture device

```text
/dev/video0
```

Selection evidence:

- exposes `Video Capture`
- exposes `Streaming`
- enumerates valid capture formats
- belongs to the USB FHD camera
- `/dev/video1` did not enumerate usable capture formats

### Device permissions

```text
/dev/video0
owner: root
group: video
GID: 44
mode: 0660
```

Host group:

```text
video:x:44:ubuntu
```

The `ubuntu` user belongs to the `video` group.

### Supported capture modes

MJPEG:

```text
640x480    @ 30 FPS
800x480    @ 30 FPS
800x600    @ 30 FPS
1024x576   @ 30 FPS
1280x720   @ 30 FPS
1600x896   @ 30 FPS
1920x1080  @ 30 FPS
```

YUYV:

```text
640x480    @ 30 FPS
800x480    @ 25 FPS
800x600    @ 15 FPS
1024x576   @ 10 FPS
1280x720   @ 10 FPS
1600x896   @ 7.5 FPS
1920x1080  @ 5 FPS
```

### Recommended camera configuration

Based on the hardware evidence:

```text
device:     /dev/video0
format:     MJPEG
resolution: 1280x720
fps:        30
```

The existing `1280x720 @ 15 FPS` configuration is not advertised as a native V4L2 mode. If 15 FPS is retained, it must be verified as an application/uStreamer-level output limit rather than a native camera capture mode.

**Observed on the target (runtime):** uStreamer's default `YUYV` at 1280×720
does NOT deliver frames (`CAP: Device select() timeout`, `source.online:false`);
the container entry point therefore defaults to `CAMERA_FORMAT=MJPEG` (the
native mode above), and `/camera/` streams once MJPEG is used. `CAMERA_FORMAT`,
`CAMERA_RESOLUTION`, `CAMERA_FPS`, `CAMERA_QUALITY` are operator-tunable.

### Approved Docker mapping

Based on current host evidence:

```yaml
ustreamer:
  devices:
    - "/dev/video0:/dev/video0"
  group_add:
    - "44"
```

Container-level access is **verified on the target**: uStreamer starts with the
mapped `/dev/video0` (group `video`, GID 44) and serves MJPEG through the nginx
`/camera/` path (camera pipeline working on the real Pi). Note: uStreamer must
use `CAMERA_FORMAT=MJPEG` (default) — `YUYV` at 1280×720 timed out on this
camera.

## GPIO

Detected character devices (probe evidence):

```text
/dev/gpiochip0   mode=600 owner=root group=root gid=0
/dev/gpiochip1   mode=600 owner=root group=root gid=0
```

Both chips are root-only. `/dev/gpiomem` was **not** observed on this target
(Ubuntu 22.04); it is not required anyway — the repository uses the
character-device API, never gpiomem.

Host groups observed:

```text
dialout:x:20:ubuntu
video:x:44:ubuntu
```

`ubuntu` belongs to `dialout` (20) and `video` (44). There is **no `gpio`
group** on the target.

### Which chip drives the header lines (RECORDED)

`gpiodetect` evidence:

```text
gpiochip0 [pinctrl-bcm2711] (58 lines)
gpiochip1 [raspberrypi-exp-gpio] (8 lines)
```

**Decision: `GPIO_DEVICE=/dev/gpiochip0`.** `gpiochip0` is the BCM2711 pin
controller (58 lines) that backs the 40-pin header, so the configured lines
(23, 24, 21, 20, 12, 13) are line offsets on `gpiochip0`. `gpiochip1`
(`raspberrypi-exp-gpio`, 8 lines) is the firmware/EXP controller and is not
used. (Optional extra confirmation: `gpioinfo gpiochip0 | grep -E 'line
+(12|13|20|21|23|24):'` — unclaimed header lines may show as `unnamed`.)

### GPIO status (RECORDED — permission decision made on the target)

```text
host group:  gpio:x:997:
udev rule:   /etc/udev/rules.d/99-cardrone-gpio.rules
             SUBSYSTEM=="gpio", KERNEL=="gpiochip0", GROUP="gpio", MODE="0660"
device:      crw-rw---- 1 root gpio /dev/gpiochip0  (mode 0660)
```

Decision: the non-root backend container receives GID **997** via
`group_add` and the chip via a `devices` mapping in
`docker-compose.raspberry.yml` (no `privileged`, only `gpiochip0`; `gpiochip1`
stays root-only). `GPIO_GID=997` and `GPIO_DEVICE=/dev/gpiochip0` are recorded
in `.env.raspberry.example`. This is a host permission decision; it grants no
other elevation.

**Container access verified on the target** (`docker compose run --rm
--no-deps --entrypoint sh backend`): `uid=1654(app) gid=1654(app)
groups=1654(app),997`; `/dev/gpiochip0` `crw-rw---- 1 root 997`; `READ OK` and
`WRITE OK`. The real backend container can drive the GPIO chip as a non-root
user.

## PWM

Probe evidence:

```text
/sys/class/pwm/pwmchip* : none found
/boot/firmware/config.txt (and /boot/config.txt): no PWM dtoverlay line
```

Hardware PWM is **not exposed** on this target. Real speed control therefore
remains a prerequisite: the operator may enable it (for example a
`dtoverlay=pwm-2chan` line in the boot config + reboot — a host configuration
change with pin-function implications), after which the PWM sysfs chip and its
channels must be re-probed and recorded. The repository does not enable it and
does not invent channel values. Without it, asserting `PwmMapping` under
`HARDWARE_MODE=real` aborts at startup (fail-fast, D5).

## I2C

Probe evidence (2026-10-06):

```text
/dev/i2c-1      crw-rw---- 1 root i2c 89, 1      (mode 0660, group i2c)
host group:     i2c:x:119:
kernel module:  i2c_bcm2835
i2cdetect:      not installed (the probe reads registers directly)
```

Decisions (Task 41 — battery monitoring, `specs/hardware/battery-monitoring.md`):

- The backend reads the INA219 over `/dev/i2c-1`. The device is **already
  group-writable** (`crw-rw---- root i2c`), so granting the non-root backend
  container gid **119** via `group_add` gives it access with **no host udev
  change** (unlike `gpiochip0`, which was root-only). Docker mapping:
  `devices: ["${I2C_DEVICE}:/dev/i2c-1"]`, `group_add: ["${I2C_GID}"]`.
- **INA219 address (RECORDED, probe 2026-10-06): `0x42`** — the register probe
  (read-only, run on the host) observed a device at `0x42` with the INA219
  default config register `0x399F`, no conversion overflow, and
  `bus_voltage_raw=0x341A` (**6.668 V** measured). This address is operator
  evidence and overrides the module-default 0x40. Shunt/calibration registers
  read 0 (no calibration written) → current/power are not meaningful and are
  out of scope (Task 41).
- The voltage window is the operator's pack: 2S 18650 Li-ion, full `8.4` V /
  empty `6.0` V (`BATTERY_FULL_VOLTAGE` / `BATTERY_EMPTY_VOLTAGE`).
- The % is a linear voltage approximation; the electrical behaviour of the pack
  and charger is **not** measured or claimed here.

**Host register read: VERIFIED** on the target (probe, 2026-10-06).
**Container access + `GET /api/battery`: VERIFIED** on the target (2026-10-06):
the non-root backend container (supplementary gid 119) reads the INA219 over
`/dev/i2c-1` with `BATTERY_MODE=ina219`; the endpoint returned
`{"available":true,"voltage":6.472,"percent":20,"state":"low","simulated":false}`
(the pack then read ≈6.47 V, i.e. ~20 % — at the configured `Low` threshold;
the earlier probe read 6.668 V ≈28 %). Direct on `:5080` and through nginx
`:8081`.
**Current/power (Task 42, `BATTERY_SHUNT_OHMS=0.1`): VERIFIED** (2026-10-06) —
the endpoint now returns `current` and `power`; five 1 s samples showed a
stable signed reading of ≈6.17 V / ≈−1.0…−1.12 A / ≈−6.3…−6.9 W (≈7 %,
`critical`). **Sign convention CONFIRMED by operator bench observation
(2026-10-06): negative current = charging, positive = discharging** — so the
≈−1.1 A reading is a **charge current ≈1.0–1.1 A** (~6.5 W) at a low pack
voltage. Honest limitation recorded: the INA219's default PGA range (±320 mV
at R100 = **±3.2 A**) saturated during a load transient (`current:3.2` clip;
the user's reference notes "counter overflow occurs at 3.2 A").
Physical state-of-charge accuracy / charger interaction remain
`NOT VERIFIED`.

## Pending verification

- enable/expose hardware PWM on the target (or defer speed), then record the
  PWM chip and channel→GPIO mapping (and its non-root write permissions)
- verify real GPIO actuation from the backend container on the target
  (`HARDWARE_MODE=real`; still `NOT VERIFIED`)
- read the INA219 from the target (`BATTERY_MODE=ina219`): host register read
  VERIFIED (probe, 0x42, 6.668 V) and container/endpoint read VERIFIED
  (2026-10-06, returned ≈6.47 V / 20 % / low); physical state-of-charge/charger
  behaviour stays `NOT VERIFIED`

## Repository implementation decisions (repo-side, no hardware needed)

These are decisions made in this repository to prepare `HARDWARE_MODE=real`;
they require no physical verification and are not target claims:

- **GPIO interface chosen**: the kernel GPIO character-device interface
  (`/dev/gpiochipN`, line-handle ioctl UAPI v1). `RaspberryGpioPlatform`
  (Infrastructure, real-mode only) opens the chip configured by
  `Raspberry:GPIO:ChipPath` (env `GPIO_DEVICE`, default `/dev/gpiochip0` — the
  device observed on the target below) and uses the validated GPIO identifiers
  as line offsets. `/dev/gpiemom` is NOT assumed or used.
- **PWM interface chosen**: the kernel PWM sysfs interface
  (`/sys/class/pwm/pwmchipN`). `RaspberryPwmPlatform` (Infrastructure,
  real-mode only) uses hardware PWM only — no software PWM with application
  busy loops. Chip path and identifier→channel mapping are operator values
  (`Raspberry:PWM:ChipPath`, `Raspberry:PWM:Channels:<id>`; envs
  `PWM_CHIP_PATH`, `PWM_CHANNEL_12`, `PWM_CHANNEL_13`) with no invented default.
- **Registration**: the concrete platforms register only for
  `HARDWARE_MODE=real`; mock/dry-run never resolve them (the dry-run
  composition guard aborts if a real sink is present).
- **Fail-fast**: asserted mappings in real mode for which the required operator
  PWM config is missing abort at startup (D5); invalid/missing GPIO config and
  invalid motor/PWM mappings already abort; an inaccessible GPIO chip fails
  loudly through the operational-failure channel (503/Unavailable) at first
  use — never a silent fallback.

## Target discovery procedure (run on the REAL Raspberry Pi)

Read-only enumeration produces the evidence records; no value below is
guessed. Run the probe on the target Pi either directly or from a
least-privilege, read-only container (the container mounts `/sys` and `/dev`
read-only, sees the same kernel/sysfs, and writes nothing):

```sh
# on the Pi host:
sh docs/hardware/discover-target.sh

# or read-only container (no privileged, no writes):
docker run --rm \
  -v /sys:/sys:ro \
  -v /dev:/dev:ro \
  -v "$PWD":/probe:ro \
  debian:bookworm-slim sh /probe/docs/hardware/discover-target.sh
```

Attach the probe output to this document as the evidence for:

- OS/kernel/arch and Docker engine facts;
- the GPIO node permissions and available groups (whether a non-root
  `gpiochip0` map is possible on THIS target);
- the PWM sysfs chips, their `npwm` and existing outputs, and any PWM
  `dtoverlay` line (channel→GPIO12/13 mapping must be confirmed from target
  evidence — NOT VERIFIED by the probe);
- the camera V4L2 nodes.

**Boundary:** the probe discovers; it never decides. Container/`docker run`
cannot auto-fill `GPIO_GID`, `PWM_CHIP_PATH`, or `PWM_CHANNEL_*` — those are
operator configuration values that come FROM this evidence (permissions
decisions and device-tree mapping are host facts, not container choices), and
real actuation additionally requires the D7 operator assertion and, for
motors, `CONFIRMED` wiring rows.

No value in this section is invented by the repository; the operator supplies
it from target evidence before real GPIO/PWM can actuate:

```text
GPIO_DEVICE=/dev/gpiochip0        # observed on target; confirm container path
GPIO_GID=<supplied when the gpiochip permission decision is recorded>
                                  # (target gpiochip0 is mode 0600 root:root,
                                  #  no `gpio` group — not yet container-accessible)
PWM_CHIP_PATH=<supplied e.g. /sys/class/pwm/pwmchipN when PWM evidence exists>
PWM_CHANNEL_12=<channel for GPIO PWM1 (12) when target evidence exists>
PWM_CHANNEL_13=<channel for GPIO PWM2 (13) when target evidence exists>
```

These are passed through Compose (`docker-compose.yml`) to the backend's
`Raspberry:*` configuration and used in `docker-compose.raspberry.yml` only
when the operator records the corresponding mapping.

## Verified target evidence (already known)

- Target: Raspberry Pi 4 Model B Rev. 1.1, **Ubuntu 22.04.5 LTS** (kernel
  5.15.0-1108-raspi, aarch64), Docker Engine 28.1.1.
- GPIO: `/dev/gpiochip0` `[pinctrl-bcm2711]` (58 lines — the 40-pin header
  bank) and `/dev/gpiochip1` `[raspberrypi-exp-gpio]` (8 lines — firmware/EXP,
  unused), both mode 0600 owner root group root; host groups `dialout` (20) and
  `video` (44); no `gpio` group. `/dev/gpiomem` not observed (and not required
  by the repository). `GPIO_DEVICE=/dev/gpiochip0` (evidence-confirmed).
- PWM: not exposed — no `/sys/class/pwm/pwmchip*`, no PWM `dtoverlay` line.
- GPIO permissions: host group `gpio:x:997:` + udev rule giving
  `/dev/gpiochip0` mode 0660 (`crw-rw---- root gpio`); the backend container
  receives GID 997 via `group_add` (no `privileged`).
- Docker Compose v2.35.1 on the target (profiles supported).
- Camera: `/dev/video0` (group `video`, GID 44, mode 0660), approved Docker
  mapping `devices: ["/dev/video0:/dev/video0"]`, `group_add: ["44"]`.
  (Many other `/dev/video*` nodes are the platform ISP/codec stack.)
- Camera config recommendation: MJPEG 1280x720 @ up to 30 FPS (15 FPS is not a
  native mode — application-level limit to verify).
- I2C: `/dev/i2c-1` `crw-rw---- root i2c` (mode 0660, group `i2c` GID 119),
  module `i2c_bcm2835` loaded. The device is already group-writable, so the
  container mapping `devices: ["/dev/i2c-1:/dev/i2c-1"]` + `group_add: ["119"]`
  needs no host udev change. INA219 **runtime VERIFIED** (2026-10-06): probe
  address `0x42`, config `0x399F`, measured 6.668 V on the host; the deployed
  Docker backend (non-root, gid 119) returned `GET /api/battery` ≈6.47 V /
  20 % / low.

## Evidence capture (paste the probe output here)

Execute `docs/hardware/discover-target.sh` on the real Pi (host or read-only
container — see "Target discovery procedure") and paste its output below, then
fill the decision table from that evidence. Nothing in this block is guessed.

```text
Linux ubuntu 5.15.0-1108-raspi #111-Ubuntu SMP PREEMPT Mon Aug 10 15:28:28 UTC 2026 aarch64 aarch64 aarch64 GNU/Linux
Ubuntu 22.04.5 LTS 5.15.0-1108-raspi
--- gpio ---
/dev/gpiochip0 mode=600 owner=root group=root gid=0
/dev/gpiochip1 mode=600 owner=root group=root gid=0
--- groups ---
dialout:x:20:ubuntu
video:x:44:ubuntu
--- pwm ---
(no pwmchip)
--- video ---
/dev/video0 mode=660 group=video gid=44
/dev/video1 mode=660 group=video gid=44
/dev/video10..16,18,20..23,31 mode=660 group=video gid=44
--- pwm overlay ---
(no pwm dtoverlay line)
--- docker ---
server=28.1.1
```

| Decision | Value from evidence | Status |
| --- | --- | --- |
| OS / kernel / Docker details | Ubuntu 22.04.5 LTS; kernel 5.15.0-1108-raspi aarch64; Docker 28.1.1 | RECORDED |
| GPIO character devices | `/dev/gpiochip0` `[pinctrl-bcm2711]` (58 lines, header bank), `/dev/gpiochip1` `[raspberrypi-exp-gpio]` (8 lines), both `0600 root:root` | RECORDED |
| GPIO group exists (`gpio`) | `gpio:x:997:` (created on target) | RECORDED |
| `GPIO_GID` | `997` | RECORDED |
| Which chip has header lines 20/21/23/24/12/13 | `gpiochip0` (`pinctrl-bcm2711`) → `GPIO_DEVICE=/dev/gpiochip0` | RECORDED |
| GPIO container access (non-root) | `app` uid 1654 + supplementary gid 997; `/dev/gpiochip0` `crw-rw---- root 997`; READ/WRITE OK | VERIFIED (Pi runtime) |
| PWM subsystem present | no `pwmchip`, no PWM dtoverlay → hardware PWM not exposed | ACTION REQUIRED (enable `dtoverlay` or defer speed) |
| `PWM_CHIP_PATH` / `PWM_CHANNEL_12/13` | — | NOT VERIFIED (until PWM is enabled and re-probed) |
| Camera `/dev/video0` (group `video` 44) | present, mode 660 group video | RECORDED |
| Camera container access (`/dev/video0`) | uStreamer starts with the mapped device and serves MJPEG at `/camera/` | VERIFIED (Pi runtime) |

## NOT VERIFIED (physical/runtime — deferred to Task 36)

- Real GPIO actuation, PWM output, and any physical motor/L298N behavior
  (wiring rows and PWM envelope remain unconfirmed in `docs/hardware/WIRING.md`).
- PWM subsystem's chip/channel mapping (PWM is not enabled on the target yet).
- Camera stream latency/quality on the target (the MJPEG pipeline itself is
  verified).
- Full Raspberry Pi OS runtime behavior of the stack on this Ubuntu 22.04 target.
- INA219 read on the target (address, container access to `/dev/i2c-1`, and a
  plausible pack voltage from `GET /api/battery`); battery/charger electrical
  behaviour and the linear state-of-charge approximation (Task 41).
