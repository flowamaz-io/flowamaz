# Starts Flowamaz locally for the Chrome E2E run (Windows PowerShell 7+).
#   .\prompts\e2e-chrome\start-local.ps1          start everything
#   .\prompts\e2e-chrome\start-local.ps1 stop     stop API + web
# Optional: $env:ANTHROPIC_KEY = "sk-ant-..." first to exercise the AI creation journeys (J11-J13).
param([string]$Action = "start")
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\..\.."
$Logs = Join-Path $Root "prompts\e2e-chrome\results\logs"
New-Item -ItemType Directory -Force -Path $Logs | Out-Null

# The pid files hold the `dotnet run` / `npx` launchers; the real servers are their children, so
# stop the launchers' process trees AND whatever still listens on the app ports.
function Get-PortPids([int]$Port) {
  (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
}
function Stop-App {
  foreach ($f in "api.pid","web.pid") {
    $p = Join-Path $Logs $f
    if (Test-Path $p) { & taskkill /PID (Get-Content $p) /T /F *> $null; Remove-Item $p }
  }
  foreach ($port in 8307, 5173) { foreach ($procId in (Get-PortPids $port)) { & taskkill /PID $procId /T /F *> $null } }
  Start-Sleep 1
  if ((Get-PortPids 8307) -or (Get-PortPids 5173)) { Write-Host "Could not free ports 8307/5173. Check with: Get-NetTCPConnection -LocalPort 8307,5173"; return $false }
  return $true
}

if ($Action -eq "stop") {
  if (Stop-App) { Write-Host "Stopped API and web (ports 8307 and 5173 are free)." }
  exit 0
}

if ((Get-PortPids 8307) -or (Get-PortPids 5173)) {
  Write-Host "0/4 Ports 8307/5173 are in use - stopping the previous Flowamaz API/web first..."
  Stop-App | Out-Null
}

Write-Host "1/4 Postgres + Redis (Docker)..."
docker compose -f "$Root\infrastructure\docker-compose.dev.yml" up -d
for ($i = 0; $i -lt 30; $i++) { docker exec flowamaz-dev-db pg_isready -U flowamaz *> $null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 2 }

Write-Host "2/4 API on http://localhost:8307 (migrates on start; first build takes a minute)..."
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:8307"
$env:DB_CONNECTION_STRING = "Host=localhost;Port=5442;Database=flowamaz;Username=flowamaz;Password=flowamaz_dev"
$env:REDIS_CONNECTION_STRING = "localhost:6379"
$env:EDITION = "pro"
$env:CORS_ALLOWED_ORIGINS = "http://localhost:5173"
$env:GIT_REPOS_BASE_PATH = Join-Path $Root "prompts\e2e-chrome\results\git-repos"
$env:CREDENTIAL_MASTER_KEY = "e2e-local-only-credential-master-key-0123456789abcdef"   # dev only
$env:GATE_SIGNING_KEY = "e2e-local-only-gate-signing-key-0123456789abcdef0123"          # dev only
$env:Ai__AllowStubWithoutKey = "true"
if ($env:ANTHROPIC_KEY) {
  try {
    Invoke-WebRequest https://api.anthropic.com/v1/models -UseBasicParsing -TimeoutSec 10 `
      -Headers @{ "x-api-key" = $env:ANTHROPIC_KEY; "anthropic-version" = "2023-06-01" } | Out-Null
    $env:Ai__AnthropicPlatformKey = $env:ANTHROPIC_KEY; Write-Host "    (Anthropic key verified - AI journeys enabled)"
  } catch {
    Write-Host "    WARNING: Anthropic rejected ANTHROPIC_KEY. Starting in AI stub mode; J11-J13 will be BLOCKED. Fix the key and re-run."
  }
}
$api = Start-Process dotnet -ArgumentList "run","--project","Flowamaz.Api","--no-launch-profile" -WorkingDirectory "$Root\backend" `
  -RedirectStandardOutput "$Logs\api.log" -RedirectStandardError "$Logs\api.err.log" -PassThru -WindowStyle Hidden
$api.Id | Out-File "$Logs\api.pid"
for ($i = 0; $i -lt 90; $i++) { try { Invoke-WebRequest http://localhost:8307/health -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 3 } }

Write-Host "3/4 Web on http://localhost:5173..."
if (-not (Test-Path "$Root\web\node_modules")) { Push-Location "$Root\web"; npm ci; Pop-Location }
$web = Start-Process npx.cmd -ArgumentList "vite","--port","5173" -WorkingDirectory "$Root\web" `
  -RedirectStandardOutput "$Logs\web.log" -RedirectStandardError "$Logs\web.err.log" -PassThru -WindowStyle Hidden
$web.Id | Out-File "$Logs\web.pid"
for ($i = 0; $i -lt 30; $i++) { try { Invoke-WebRequest http://localhost:5173/api/v1/templates -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 2 } }

Write-Host "4/4 Ready. Logs in $Logs"
Write-Host "Next: in the repo root run  claude --chrome  and paste the prompt from prompts\e2e-chrome\README.md"
