# Raspberry Pi Runbook — arranque gradual de la aplicación

Guía paso a paso para llevar CarDrone a una Raspberry Pi real. Objetivo: subir
el stack de forma **segura y verificable**, sin inventar valores y sin activar
salidas de hardware hasta que exista evidencia. Referencias:
`docs/hardware/raspberry-pi-inventory.md` (evidencia), `.env.raspberry.example`
(template), `docs/DOCKER.md` (builds), `specs/deployment/raspberry-mode.md`
(verificación física, Task 36).

## Prerrequisitos

- Raspberry Pi (el inventario registra una Pi 4 Model B) con Raspberry Pi OS
  ARM64 (Debian Bookworm) y Docker Engine + Compose v2.
- Cámara USB conectada (para el stream; opcional para la UI).
- Copiar la carpeta del proyecto a la Pi (sin `node_modules`, `bin`, `obj`,
  `dist`).

## Paso 1 — Sonda de descubrimiento (evidencia)

En la Pi, ejecuta la sonda (read-only; también se puede desde un contenedor
read-only, ver el inventario):

```sh
sh docs/hardware/discover-target.sh
```

Pega la salida como evidencia en el inventario (bloque "Evidence capture") y
anota las decisiones junto a ella: grupo `gpio` (¿existe / lo creas?),
`GPIO_GID`, `PWM_CHIP_PATH` y `PWM_CHANNEL_12/13` si aparecen, canal de la
cámara (`/dev/video0`).

## Paso 2 — Escribir el `.env`

Copiar la plantilla y completar **solo** los campos de operador con
evidencia; dejar el resto tal cual:

```sh
cp .env.raspberry.example .env
```

Durante el arranque gradual usa la línea `HARDWARE_MODE` indicada en cada
paso (la plantilla trae `real` por defecto — cámbiala temporalmente).

## Paso 3 — Revisar la configuración renderizada

```sh
docker compose config            # por defecto: frontend + backend solamente
docker compose --profile camera config   # con profile: aparece ustreamer
```

Comprueba: solo `frontend` + `backend` visible por defecto; con el profile
`camera` aparece `ustreamer`; sin devices inventados salvo el mapping de
cámara ya aprobado.

## Paso 4 — Primer arranque seguro (`mock`)

Edita `.env` → `HARDWARE_MODE=mock` y levanta:

```sh
docker compose up -d
```

Verifica:
- `curl localhost:8081/api/health` → 200.
- `curl localhost:8081/` → SPA (200).
- `curl localhost:8081/camera-mode.json` → `{"mode":"mock"}` y `/camera/` → 404.
- UI disponible en `http://<ip-de-la-pi>:8081` (conectar, comandos, velocidad).

## Paso 5 — Cámara (`ustreamer`)

Con `CAMERA_MODE=ustreamer` y el mapping de cámara ya en el overlay (keep en
el inventory):

```sh
docker compose --profile camera up -d
```

Verifica: `GET /camera-mode.json` → `{"mode":"ustreamer"}`; `GET /camera/`
devuelve MJPEG (un navegador muestra vídeo); el backend reporta
`DroneStatus.camera = streaming`; al desconectar la cámara pasa a `error`.

## Paso 6 — Batería (INA219)

Con `BATTERY_MODE=ina219` y los valores de operador del probe I2C
(`BATTERY_I2C_ADDRESS=0x42`, `BATTERY_FULL_VOLTAGE`, `BATTERY_EMPTY_VOLTAGE`;
ver el inventario, sección "I2C"):

- **Docker**: el overlay `backend` ya mapea `I2C_DEVICE`/`I2C_GID`. El target
  tiene `/dev/i2c-1` `crw-rw---- root i2c` (grupo 119), así que `group_add`
  basta — **no** requiere cambio de udev (a diferencia de `gpiochip0`).
- **Nativo**: el backend corre como root y accede a `/dev/i2c-1` directamente.
  Los valores van en `battery.env` (mismo mecanismo que `motor.env`/`pwm.env`).
- Verifica: `curl localhost:8081/api/battery` devuelve la tensión del pack
  (p. ej. `{"available":true,"voltage":7.8,"percent":75,"state":"ok",
  "simulated":false}`); con el bus inaccesible devuelve
  `{"available":false,"state":"error"}` — nunca un 5xx.
- Sin `BATTERY_MODE` (o en `mock`) la UI muestra la lectura simulada marcada
  `SIMULATED` (honestidad: nunca se presenta como sensor real).
- El % es una **aproximación lineal** voltaje↔carga (no es un fuel gauge);
  corriente/potencia (shunt) quedan fuera de alcance de la Task 41.

## Paso 7 — Ruta GPIO/PWM sin riesgo (`dry-run`)

`.env` → `HARDWARE_MODE=dry-run` (misma Pi, mismo gráfico, salidas grabadas y
suprimidas):

```sh
docker compose up -d
```

Verifica: el backend arranca sin error de preflight, el provider reporta
disponibilidad por software, y los comandos/speed producen registros
`DryRunOperationRecord` sin actuación. Es la prueba de que todo el stack
GPIO/PWM **de software** funciona en la Pi antes de tocar hardware.

## Paso 8 — Paso a `real` (solo con precondiciones)

Cambia a `HARDWARE_MODE=real` **solo cuando**:

- [ ] Permisos de `gpiochip0` resueltos en la Pi (grupo `gpio`/udev) y
      `GPIO_GID` en `.env` + mapping en `docker-compose.raspberry.yml`
      (ver seam documentado del `backend`).
- [ ] PWM configurado con evidencia de la sonda: `PWM_CHIP_PATH` y
      `PWM_CHANNEL_12/13` en `.env` (si vas a usar velocidad).
- [ ] Para motores: `WIRING.md` con las filas `CONFIRMED` y
      `MotorMapping`/`PwmMapping` asertados en configuración.
- [ ] `SAFETY_EXTERNAL_FAILURE_PROTECTION_VERIFIED=true` **solo después** de
      documentar la protección externa ante fallo brusco (D7).

Sin esas precondiciones `real` arranca pero GPIO/PWM devuelven `Unavailable`
(genuino) o abortan con el mensaje de configuración — nunca falla en silencio.

## Paso 9 — Verificación final y registro

- Endpoints de control (connect/command/speed/status, 400/409) sobre el
  backend real.
- `GET /camera/mode.json` + stream; comportamiento de reconexión de la UI.
- Frecuencia/PWM y dirección de motores en banco con el registro de evidencia
  (no basta "funciona": anota fecha + observación por fila).

## Paso 10 — Resultado honesto

Todo lo que no se verificó en este arranque queda marcado `NOT VERIFIED`
(actuación real, acceso en contenedor a gpiochip/Video, MJPEG real, detalles
OS/kernel). Rellena estos campos solo con evidencia real; el backlog mantiene
el Task 36 `BLOCKED` hasta entonces.

## Paso 11 — Alternativa nativa (sin Docker) y despliegue híbrido

Cuando se necesita escribir `/sys/class/pwm` (velocidad PWM real), el
contenedor no sirve: Docker monta `/sys` en solo lectura. La alternativa es
ejecutar el stack **nativo** en la Pi:

```bash
sudo apt-get install -y nginx ustreamer libgpiod2 gpiod
chmod +x scripts/run-native-pi.sh
cp scripts/native.env.example native.env       # HARDWARE_MODE, CAMERA_MODE, MOTOR_ENABLE_PINS...
sudo ./scripts/run-native-pi.sh                # uStreamer + backend + nginx
sudo ./scripts/run-native-pi.sh --stop         # parar
```

El stack nativo **no arranca solo tras un reinicio** (nginx sí, por ser servicio
del sistema). Para que suba al boot, instala una vez el servicio incluido:

```bash
sudo ./scripts/run-native-pi.sh --stop         # parar la instancia manual
sudo cp scripts/cardrone-native.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now cardrone-native.service
```

- nginx sirve `frontend/dist` y hace de proxy `/api/`→5080 y
  `/camera/`→uStreamer (8080). Genera `camera-mode.json` desde `CAMERA_MODE`.
- El backend se ejecuta **self-contained linux-arm64** (no requiere .NET 10 en
  la Pi, cuyo SDK es 6.0.100).
- `MOTOR_ENABLE_PINS=12,13` retiene `ENA`/`ENB` en ALTO con
  `gpioset -m signal` (sin puentes físicos).

### Despliegue híbrido (recomendado para desarrollar)

Fuente de verdad y builds en el PC; la Pi solo ejecuta artefactos:

```powershell
# en el PC (Windows)
./scripts/deploy-pi.ps1 -PiHost <IP_PI>
# -SkipBuild / -SkipRestart para acotar
```

`deploy-pi.ps1` publica el backend, construye el frontend same-origin, copia
`backend/publish`, `frontend/dist` y `scripts/` por SSH y reinicia con
`sudo -n`. Requiere una entrada `sudoers` acotada a ese script:

```bash
echo 'ubuntu ALL=(ALL) NOPASSWD: /home/ubuntu/CarDrone/scripts/run-native-pi.sh' | sudo tee /etc/sudoers.d/cardrone-native
sudo chmod 440 /etc/sudoers.d/cardrone-native && sudo visudo -c
```

> Lección (target): el binario self-contained debe ejecutarse con CWD = su
> carpeta de `publish`; si se lanza desde la raíz del repo, ASP.NET no
> encuentra `appsettings.json`, la sección `GPIO` queda vacía y `real` aborta
> con `Required GPIO configuration value 'Pin1' is missing`.
> `run-native-pi.sh` ya lo hace.