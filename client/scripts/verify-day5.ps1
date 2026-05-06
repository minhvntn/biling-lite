param(
    [string]$TaskName = "ServerManagerBillingAgent",
    [string]$ServiceName = "ServerManagerBillingWatchdog",
    [string]$InstallRoot = "C:\Program Files\ServerManagerBilling"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Day5 Verify ==="

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "[FAIL] Service not found: $ServiceName"
} else {
    Write-Host "[OK] Service: $($service.Name) Status=$($service.Status)"
}

cmd /c "schtasks /Query /TN ""$TaskName"" /FO LIST >nul 2>nul"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] Scheduled task not found: $TaskName"
} else {
    Write-Host "[OK] Scheduled task exists: $TaskName"
}

$agentExe = Join-Path $InstallRoot "Agent\Client.Agent.Wpf.exe"
$serviceExe = Join-Path $InstallRoot "Service\Client.Watchdog.Service.exe"
if (Test-Path -LiteralPath $agentExe) {
    Write-Host "[OK] Agent exe found"
} else {
    Write-Host "[FAIL] Agent exe missing"
}

if (Test-Path -LiteralPath $serviceExe) {
    Write-Host "[OK] Service exe found"
} else {
    Write-Host "[FAIL] Service exe missing"
}

$runningAgent = Get-Process -Name "Client.Agent.Wpf" -ErrorAction SilentlyContinue
if ($runningAgent) {
    Write-Host "[OK] Agent process running (count=$($runningAgent.Count))"
} else {
    Write-Host "[WARN] Agent process is not running right now"
}

$logRoot = Join-Path $env:ProgramData "ServerManagerBilling\logs"
$agentLog = Join-Path $logRoot "client-agent.log"
$watchdogLog = Join-Path $logRoot "watchdog-service.log"
if (Test-Path -LiteralPath $agentLog) {
    Write-Host "[OK] Agent log exists"
} else {
    Write-Host "[WARN] Agent log not found yet"
}

if (Test-Path -LiteralPath $watchdogLog) {
    Write-Host "[OK] Watchdog log exists"
} else {
    Write-Host "[WARN] Watchdog log not found yet"
}
