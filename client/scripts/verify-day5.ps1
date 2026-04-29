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
Write-Host (Test-Path $agentExe ? "[OK] Agent exe found" : "[FAIL] Agent exe missing")
Write-Host (Test-Path $serviceExe ? "[OK] Service exe found" : "[FAIL] Service exe missing")

$runningAgent = Get-Process -Name "Client.Agent.Wpf" -ErrorAction SilentlyContinue
if ($runningAgent) {
    Write-Host "[OK] Agent process running (count=$($runningAgent.Count))"
} else {
    Write-Host "[WARN] Agent process is not running right now"
}

$logRoot = Join-Path $env:ProgramData "ServerManagerBilling\logs"
$agentLog = Join-Path $logRoot "client-agent.log"
$watchdogLog = Join-Path $logRoot "watchdog-service.log"
Write-Host (Test-Path $agentLog ? "[OK] Agent log exists" : "[WARN] Agent log not found yet")
Write-Host (Test-Path $watchdogLog ? "[OK] Watchdog log exists" : "[WARN] Watchdog log not found yet")
