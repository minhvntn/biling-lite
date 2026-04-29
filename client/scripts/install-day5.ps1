param(
    [string]$AgentExePath = "C:\Program Files\ServerManagerBilling\Agent\Client.Agent.Wpf.exe",
    [string]$ServiceExePath = "C:\Program Files\ServerManagerBilling\Service\Client.Watchdog.Service.exe",
    [string]$TaskName = "ServerManagerBillingAgent",
    [string]$ServiceName = "ServerManagerBillingWatchdog"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "Please run this script as Administrator."
}

if (-not (Test-Path $AgentExePath)) {
    throw "Agent executable not found: $AgentExePath"
}
if (-not (Test-Path $ServiceExePath)) {
    throw "Service executable not found: $ServiceExePath"
}

Write-Host "Creating or updating scheduled task $TaskName ..."
cmd /c "schtasks /Create /TN \"$TaskName\" /TR \"$AgentExePath\" /SC ONLOGON /RL HIGHEST /F /IT"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create/update scheduled task $TaskName"
}

Write-Host "Recreating service $ServiceName ..."
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    cmd /c "sc delete \"$ServiceName\" >nul 2>nul"
    Start-Sleep -Seconds 1
}

cmd /c "sc create \"$ServiceName\" binPath= \"$ServiceExePath\" start= auto"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create service $ServiceName"
}

cmd /c "sc description \"$ServiceName\" \"ServerManagerBilling watchdog service\""

Write-Host "Starting service $ServiceName ..."
Start-Service -Name $ServiceName

Write-Host "Running scheduled task once ..."
cmd /c "schtasks /Run /TN \"$TaskName\""
if ($LASTEXITCODE -ne 0) {
    throw "Failed to run scheduled task $TaskName"
}

Write-Host "Install completed."
