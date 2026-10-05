#!/bin/sh
# ---------------------------------------------------------------------------
# CarDrone — arranque NATIVO del STACK COMPLETO en la Raspberry Pi (sin Docker):
#   nginx  : sirve frontend/dist y hace de proxy /api/ (backend) y /camera/ (uStreamer)
#   backend: ASP.NET Core (dotnet), puerto 5080
#   camera : uStreamer MJPEG, puerto interno 8080
#
# Por qué nativo: fuera de Docker el proceso SÍ puede escribir /sys/class/pwm
# (sysfs), lo que habilita el PWM real (velocidad) y el GPIO directo. Dentro de
# Docker, /sys es de solo lectura.
#
# Prerrequisitos (en la Pi):
#   sudo apt-get install -y nginx ustreamer libgpiod2
#   .NET 10 (SDK para `dotnet run`, o publish copiado en backend/publish)
#   node/npm SOLO si hay que construir el frontend (frontend/dist ausente)
#   Ejecutar con sudo (nginx + sysfs PWM):
#       sudo ./scripts/run-native-pi.sh
#   Parar:  sudo ./scripts/run-native-pi.sh --stop
# ---------------------------------------------------------------------------
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

PIDDIR="$ROOT/.native-pids"
NGINX_SITE=/etc/nginx/sites-available/cardrone

stop_all() {
  if [ -d "$PIDDIR" ]; then
    for p in "$PIDDIR"/*.pid; do
      [ -f "$p" ] || continue
      kill "$(cat "$p")" 2>/dev/null || true
      rm -f "$p"
    done
  fi
  if [ -e /etc/nginx/sites-enabled/cardrone ]; then
    rm -f /etc/nginx/sites-enabled/cardrone
    nginx -s reload 2>/dev/null || true
  fi
}

if [ "${1:-}" = "--stop" ]; then
  stop_all
  echo "CarDrone nativo detenido."
  exit 0
fi

# --- 1) Configuración de operador (exportada al proceso .NET) ---
for f in native.env motor.env pwm.env; do
  if [ -f "$f" ]; then
    # shellcheck disable=SC1090
    set -a; . "./$f"; set +a
  fi
done

: "${HARDWARE_MODE:=real}"
: "${CAMERA_MODE:=ustreamer}"
: "${GPIO_DEVICE:=/dev/gpiochip0}"
: "${BACKEND_PORT:=5080}"
: "${USTREAMER_PORT:=8080}"
: "${HTTP_PORT:=8081}"
: "${CAMERA_DEVICE:=/dev/video0}"
: "${CAMERA_FORMAT:=MJPEG}"
: "${CAMERA_RESOLUTION:=1280x720}"
: "${CAMERA_FPS:=15}"
: "${CAMERA_QUALITY:=80}"
: "${CAMERA_BUFFERS:=1}"
: "${CAMERA_WORKERS:=1}"
# ustreamer = WebRTC from the uStreamer MJPEG feed (keeps the /camera/ fallback);
# device   = WebRTC straight from /dev/video0 (lowest latency; no MJPEG fallback,
#            because the camera is exclusive and uStreamer cannot run alongside).
: "${CAMERA_SOURCE:=ustreamer}"
: "${WWW_ROOT:=/var/www/cardrone}"
: "${GO2RTC_API_PORT:=1984}"
: "${WEBRTC_ENABLED:=1}"
# go2rtc binary: explicit env, else on PATH, else the conventional local path.
if [ -z "${GO2RTC_BIN:-}" ]; then
  GO2RTC_BIN="$(command -v go2rtc 2>/dev/null || true)"
  [ -n "$GO2RTC_BIN" ] || GO2RTC_BIN="/home/ubuntu/bin/go2rtc"
fi

export HARDWARE_MODE CAMERA_MODE
export Raspberry__GPIO__ChipPath="$GPIO_DEVICE"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="http://0.0.0.0:$BACKEND_PORT"
# En nativo el probe apunta al nginx local. En modo 'device' no existe /camera/
# (uStreamer parado), así que el probe comprueba la API de go2rtc.
if [ "$CAMERA_SOURCE" = "device" ]; then
  export Camera__ProbeUrl="${Camera__ProbeUrl:-http://127.0.0.1:$HTTP_PORT/go2rtc/api/streams}"
else
  export Camera__ProbeUrl="${Camera__ProbeUrl:-http://127.0.0.1:$HTTP_PORT/camera/}"
fi

mkdir -p "$PIDDIR"

# --- 2) Prerrequisitos ---
command -v nginx >/dev/null 2>&1 || { echo "ERROR: falta nginx (sudo apt-get install -y nginx)"; exit 1; }
if [ ! -x "$ROOT/backend/publish/DroneControl.Api" ] && ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: no hay publish self-contained y falta dotnet. Publica en el PC (ver siguiente mensaje)." >&2
  exit 1
fi
ldconfig -p 2>/dev/null | grep -q 'libgpiod' || echo "AVISO: falta libgpiod.so.2 (sudo apt-get install -y libgpiod2) — GPIO real fallará."

echo "== CarDrone nativo =="
echo "   HARDWARE_MODE=$HARDWARE_MODE  CAMERA_MODE=$CAMERA_MODE  GPIO_DEVICE=$GPIO_DEVICE"
[ -f motor.env ] && echo "   motor.env=sí" || echo "   motor.env=NO (provider inerte)"
[ -f pwm.env ]   && echo "   pwm.env=sí"   || echo "   pwm.env=no (sin velocidad PWM)"

# --- 3) uStreamer ---
if [ "$CAMERA_MODE" = "ustreamer" ] && [ "$CAMERA_SOURCE" != "device" ]; then
  if command -v ustreamer >/dev/null 2>&1; then
    echo "== uStreamer :$USTREAMER_PORT ($CAMERA_DEVICE) =="
    # --buffers 1 (single device buffer) and --tcp-nodelay (disable Nagle on the
    # stream socket) keep the MJPEG latency as low as the pipeline allows.
    nohup ustreamer --host 127.0.0.1 --port "$USTREAMER_PORT" --device "$CAMERA_DEVICE" \
      --format "$CAMERA_FORMAT" --resolution "$CAMERA_RESOLUTION" \
      --desired-fps "$CAMERA_FPS" --quality "$CAMERA_QUALITY" \
      --buffers "$CAMERA_BUFFERS" --workers "$CAMERA_WORKERS" --tcp-nodelay \
      < /dev/null > "$ROOT/.native-ustreamer.log" 2>&1 &
    echo $! > "$PIDDIR/ustreamer.pid"
  else
    echo "AVISO: 'ustreamer' no instalado (sudo apt-get install -y ustreamer). CAMERA quedará fuera de línea."
  fi
fi

# --- 3c) go2rtc (WebRTC) OPCIONAL ---
# Sirve el vídeo por WebRTC (baja latencia) transcodificando la señal MJPEG de
# uStreamer a H.264 (hardware v4l2m2m). /camera/ (MJPEG) queda como respaldo.
# El navegador negocia con /go2rtc/api/webrtc (proxy de nginx, same-origin).
if [ "$CAMERA_MODE" = "ustreamer" ] && [ "$WEBRTC_ENABLED" != "0" ] && [ -x "$GO2RTC_BIN" ]; then
  if [ "$CAMERA_SOURCE" = "device" ]; then
    CAM_SOURCE="exec:ffmpeg -hide_banner -nostdin -fflags nobuffer -flags low_delay -f v4l2 -input_format mjpeg -video_size $CAMERA_RESOLUTION -framerate $CAMERA_FPS -i $CAMERA_DEVICE -c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -g 30 -b:v 2500k -f rtsp {output}"
  else
    CAM_SOURCE="exec:ffmpeg -hide_banner -nostdin -fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 -f mpjpeg -i http://127.0.0.1:$USTREAMER_PORT/stream -c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -g 30 -b:v 2500k -f rtsp {output}"
  fi
  GO2RTC_CONFIG="$ROOT/.native-go2rtc.yaml"
  sed -e "s|__CAM_SOURCE__|$CAM_SOURCE|g" "$ROOT/scripts/go2rtc.yaml" > "$GO2RTC_CONFIG"
  echo "== go2rtc WebRTC (API :$GO2RTC_API_PORT, source=$CAMERA_SOURCE) =="
  nohup "$GO2RTC_BIN" -config "$GO2RTC_CONFIG" \
    < /dev/null > "$ROOT/.native-go2rtc.log" 2>&1 &
  echo $! > "$PIDDIR/go2rtc.pid"
fi

# --- 3b) Motor enable (ENA/ENB) OPCIONAL ---
# Si MOTOR_ENABLE_PINS está definido (p.ej. "12,13"), se mantiene en ALTO
# mientras el stack corre, para habilitar el L298N cuando ENA/ENB no llevan
# puente físico. Es config-driven: sin valor no se toca ningún pin.
# Requiere gpioset (paquete gpiod). El proceso se para con --stop.
if [ -n "${MOTOR_ENABLE_PINS:-}" ]; then
  command -v gpioset >/dev/null 2>&1 || echo "AVISO: falta gpioset (sudo apt-get install -y gpiod); no se habilitan motores."
  _set=""
  for _pin in $(echo "$MOTOR_ENABLE_PINS" | tr ',' ' '); do
    _set="$_set $_pin=1"
  done
  if command -v gpioset >/dev/null 2>&1; then
    echo "== motor enable (gpioset -m signal gpiochip0$_set) =="
    # -m signal mantiene las líneas hasta recibir SIGTERM/SIGINT (--stop las libera).
    # -m wait NO sirve aquí: con stdin en /dev/null recibe EOF y sale de inmediato.
    # shellcheck disable=SC2086
    nohup gpioset -m signal gpiochip0 $_set < /dev/null > "$ROOT/.native-motorenable.log" 2>&1 &
    echo $! > "$PIDDIR/motorenable.pid"
  fi
fi

# --- 4) Frontend (build si falta dist) ---
if [ ! -f "$ROOT/frontend/dist/index.html" ]; then
  command -v npm >/dev/null 2>&1 || { echo "ERROR: falta npm para construir el frontend."; exit 1; }
  echo "== build frontend (VITE_API_BASE_URL vacío = same-origin) =="
  ( cd frontend && npm ci && VITE_API_BASE_URL= npm run build )
fi
mkdir -p "$WWW_ROOT"
# Drop stale fingerprinted bundles so old assets (and stale API bases baked into
# them) cannot linger; index.html always points at the current hash.
rm -rf "$WWW_ROOT/assets"
cp -a "$ROOT/frontend/dist/." "$WWW_ROOT/"
chmod -R a+rX "$WWW_ROOT"

# Capability document the frontend reads at startup (mirrors the Docker
# frontend entrypoint /camera-mode.json): mode = ustreamer | mock.
if [ "$CAMERA_MODE" = "ustreamer" ]; then _camera_mode=ustreamer; else _camera_mode=mock; fi
printf '{"mode":"%s"}\n' "$_camera_mode" > "$WWW_ROOT/camera-mode.json"

# --- 5) Backend ---
echo "== backend :$BACKEND_PORT =="
# IMPORTANTE: el binario debe ejecutarse con CWD = su carpeta de publish; si no,
# ASP.NET no encuentra appsettings.json (content root por defecto) y la sección
# GPIO queda vacía -> aborta en modo real. El `exec` mantiene el PID estable.
SELF="$ROOT/backend/publish/DroneControl.Api"
DLL="$ROOT/backend/publish/DroneControl.Api.dll"
APP_DIR="$ROOT/backend/publish"
LOG="$ROOT/.native-backend.log"
if [ -x "$SELF" ]; then
  echo "   ejecutable = backend/publish/DroneControl.Api (self-contained, sin dotnet del sistema)"
  ( cd "$APP_DIR" && exec nohup ./DroneControl.Api ) < /dev/null > "$LOG" 2>&1 &
elif [ -f "$DLL" ]; then
  echo "   ejecutable = dotnet backend/publish/DroneControl.Api.dll"
  ( cd "$APP_DIR" && exec nohup dotnet DroneControl.Api.dll ) < /dev/null > "$LOG" 2>&1 &
else
  echo "   ejecutable = dotnet run (requiere SDK .NET 10)"
  nohup dotnet run --project "$ROOT/backend/src/DroneControl.Api" -c Release < /dev/null > "$LOG" 2>&1 &
fi
echo $! > "$PIDDIR/backend.pid"

# --- 6) nginx ---
echo "== nginx :$HTTP_PORT =="
sed -e "s|__HTTP_PORT__|$HTTP_PORT|g" \
    -e "s|__BACKEND_PORT__|$BACKEND_PORT|g" \
    -e "s|__USTREAMER_PORT__|$USTREAMER_PORT|g" \
    -e "s|__GO2RTC_API_PORT__|$GO2RTC_API_PORT|g" \
    -e "s|__WWW_ROOT__|$WWW_ROOT|g" \
    "$ROOT/scripts/nginx-native.conf" > "$NGINX_SITE"
ln -sf "$NGINX_SITE" /etc/nginx/sites-enabled/cardrone
[ -e /etc/nginx/sites-enabled/default ] && rm -f /etc/nginx/sites-enabled/default
nginx -t
nginx -s reload 2>/dev/null || nginx

echo
echo "== CarDrone nativo ARRIBA =="
echo "   UI   : http://<IP_PI>:$HTTP_PORT"
echo "   API  : http://127.0.0.1:$BACKEND_PORT/api/health"
echo "   logs : .native-backend.log  .native-ustreamer.log"
echo "   parar: sudo $0 --stop"
echo
echo "Recuerda: para actuación en 'real' pon SAFETY__EXTERNALABRUPTFAILUREPROTECTIONVERIFIED=true en native.env."
echo "Y mantén ENA/ENB habilitados (jumpers o servicio cardrone-motor-enable) para que las ruedas giren."