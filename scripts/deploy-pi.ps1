<#
.SYNOPSIS
  CarDrone - hybrid deploy: build on the PC and deploy to the Raspberry Pi over SSH.

.DESCRIPTION
  Source of truth = this PC. The Pi only runs published artifacts:
    1. Publish the backend self-contained for linux-arm64 (no .NET 10 needed on the Pi).
    2. Build the frontend with an empty VITE_API_BASE_URL (same-origin).
    3. Copy backend/publish, frontend/dist and scripts/ to the Pi.
    4. Restart the native stack (scripts/run-native-pi.sh).

  Restart uses `sudo -n`: the Pi must have a NOPASSWD entry for the script
  (see docs/RUNBOOK-PI.md). Otherwise use -SkipRestart and restart manually.

.PARAMETER PiHost
  Pi IP or hostname. Also via the CARDRONE_PI_HOST environment variable.
.PARAMETER PiUser
  SSH user (default 'ubuntu'; CARDRONE_PI_USER).
.PARAMETER PiPath
  Repo path on the Pi (default '/home/ubuntu/CarDrone'; CARDRONE_PI_PATH).
.PARAMETER SkipBuild
  Do not rebuild; only copy what is already in backend/publish and frontend/dist.
.PARAMETER SkipRestart
  Only copy; do not restart the stack on the Pi.
.PARAMETER Docker
  Use the Docker Compose path instead of the native one: sync the SOURCE tree to
  the Pi and rebuild the images (`docker compose build`), then `up -d`. Requires
  Docker + an operator `.env` on the Pi. -SkipBuild skips the rebuild (only
  `up -d`); -SkipRestart only syncs the source.

.EXAMPLE
  ./scripts/deploy-pi.ps1 -PiHost 192.168.1.50

.EXAMPLE
  $env:CARDRONE_PI_HOST = 'raspberrypi.local'; ./scripts/deploy-pi.ps1
#>
[CmdletBinding()]
param(
  [string]$PiHost = $env:CARDRONE_PI_HOST,
  [string]$PiUser = $(if ($env:CARDRONE_PI_USER) { $env:CARDRONE_PI_USER } else { 'ubuntu' }),
  [string]$PiPath = $(if ($env:CARDRONE_PI_PATH) { $env:CARDRONE_PI_PATH } else { '/home/ubuntu/CarDrone' }),
  [switch]$SkipBuild,
  [switch]$SkipRestart,
  [switch]$Docker
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $PiHost) { throw 'Missing -PiHost (or CARDRONE_PI_HOST).' }

$sshTarget = "$PiUser@$PiHost"
$sshOpts = @('-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new')

function Invoke-Checked {
  param([string]$Label, [scriptblock]$Command)
  Write-Host "== $Label ==" -ForegroundColor Cyan
  & $Command
  if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit $LASTEXITCODE)." }
}

# --- Docker path (-Docker): sync the SOURCE tree and rebuild the images on the
#     Pi. The native path below deploys published artifacts instead; the two are
#     mutually exclusive (same ports on the Pi). ---
if ($Docker) {
  Invoke-Checked 'sync source tree to the Pi' {
    $tar = Join-Path $env:TEMP 'cardrone-src.tgz'
    if (Test-Path $tar) { Remove-Item -Force $tar }
    Push-Location $root
    try {
      # Heavy/generated dirs are excluded: the containers build from source.
      & tar -czf $tar --exclude node_modules --exclude dist --exclude publish --exclude bin --exclude obj --exclude .git --exclude pics backend frontend camera docker-compose.yml docker-compose.raspberry.yml .env.raspberry.example
    } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'tar failed for the source tree' }
    scp @sshOpts $tar "${sshTarget}:/tmp/cardrone-src.tgz"
  }
  Invoke-Checked 'extract source on the Pi' {
    # Merges over the repo; operator files (.env, native.env, motor.env, pwm.env)
    # are not part of the archive and stay untouched.
    ssh @sshOpts $sshTarget "tar -xzf /tmp/cardrone-src.tgz -C '$PiPath' && rm -f /tmp/cardrone-src.tgz"
  }

  if (-not $SkipBuild) {
    Invoke-Checked 'docker compose build on the Pi' {
      ssh @sshOpts $sshTarget "cd '$PiPath' && docker compose build"
    }
  }
  if (-not $SkipRestart) {
    Invoke-Checked 'docker compose up -d' {
      ssh @sshOpts $sshTarget "cd '$PiPath' && docker compose up -d"
    }
    Write-Host "Deployed (Docker). UI: http://${PiHost}:8081   API: http://${PiHost}:5080/api/health" -ForegroundColor Green
  } else {
    Write-Host "Source synced. Rebuild/up on the Pi: cd $PiPath && docker compose up -d --build" -ForegroundColor Yellow
  }
  return
}

# --- 1) Build (backend self-contained + frontend same-origin) ---
if (-not $SkipBuild) {
  Invoke-Checked 'publish backend linux-arm64 (self-contained)' {
    dotnet publish backend/src/DroneControl.Api -c Release -r linux-arm64 --self-contained true -o backend/publish
  }

  Push-Location frontend
  try {
    Invoke-Checked 'npm ci' { npm ci }
    # Same-origin is set by frontend/.env.production (VITE_API_BASE_URL=).
    # Do NOT rely on a PowerShell empty env var: `$env:X = ''` is dropped on
    # Windows, which silently re-enabled the http://localhost:5080 fallback and
    # made the deployed UI call the operator's PC instead of the Pi.
    Invoke-Checked 'npm run build (same-origin)' { npm run build }
  } finally { Pop-Location }
}

foreach ($artifact in @('backend/publish/DroneControl.Api', 'frontend/dist/index.html')) {
  if (-not (Test-Path $artifact)) { throw "Missing $artifact - run without -SkipBuild." }
}

# --- 2) Copy to the Pi as single tarballs with an atomic swap. Uploading one
#        file (instead of scp -r of thousands) is far less likely to stall, and
#        extracting to `.deploy` before swapping means an interrupted transfer
#        can never leave a half-published app behind (a previous interrupted
#        scp -r left backend/publish missing an assembly and crashed the app).
function Send-Tree {
  param(
    [Parameter(Mandatory)][string]$LocalDir,
    [Parameter(Mandatory)][string]$Leaf,
    [Parameter(Mandatory)][string]$RemoteDir
  )
  $tar = Join-Path $env:TEMP "cardrone-$Leaf.tgz"
  if (Test-Path $tar) { Remove-Item -Force $tar }
  $parent = Split-Path $LocalDir -Parent
  if (-not $parent) { $parent = '.' }
  Push-Location $parent
  try { tar -czf $tar $Leaf } finally { Pop-Location }
  if ($LASTEXITCODE -ne 0) { throw "tar failed for $LocalDir" }

  Invoke-Checked "upload $Leaf" { scp @sshOpts $tar "${sshTarget}:/tmp/cardrone-$Leaf.tgz" }
  Invoke-Checked "install $Leaf" {
    ssh @sshOpts $sshTarget "rm -rf '$PiPath/.deploy' && mkdir -p '$PiPath/.deploy' && tar -C '$PiPath/.deploy' -xzf '/tmp/cardrone-$Leaf.tgz' && rm -rf '$RemoteDir' && mv '$PiPath/.deploy/$Leaf' '$RemoteDir' && rm -rf '$PiPath/.deploy' && rm -f '/tmp/cardrone-$Leaf.tgz'"
  }
}

Send-Tree -LocalDir 'backend/publish' -Leaf 'publish' -RemoteDir "$PiPath/backend/publish"
Send-Tree -LocalDir 'frontend/dist' -Leaf 'dist' -RemoteDir "$PiPath/frontend/dist"
Send-Tree -LocalDir 'scripts' -Leaf 'scripts' -RemoteDir "$PiPath/scripts"

Invoke-Checked 'mark executables' {
  ssh @sshOpts $sshTarget "chmod +x '$PiPath/backend/publish/DroneControl.Api' '$PiPath/scripts/run-native-pi.sh'"
}

# --- 3) Restart the native stack (requires passwordless sudo on the Pi) ---
if (-not $SkipRestart) {
  Invoke-Checked 'restart native stack on the Pi' {
    ssh @sshOpts $sshTarget "cd '$PiPath' && sudo -n ./scripts/run-native-pi.sh --stop ; sudo -n ./scripts/run-native-pi.sh"
  }
  Write-Host "Deployed. UI: http://${PiHost}:8081   API: http://${PiHost}:5080/api/health" -ForegroundColor Green
} else {
  Write-Host "Copied. Restart on the Pi: cd $PiPath && sudo ./scripts/run-native-pi.sh" -ForegroundColor Yellow
}
