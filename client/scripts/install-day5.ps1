param(
    [string]$AgentExePath = "C:\Program Files\ServerManagerBilling\Agent\Client.Agent.Wpf.exe",
    [string]$ServiceExePath = "C:\Program Files\ServerManagerBilling\Service\Client.Watchdog.Service.exe",
    [string]$TaskName = "ServerManagerBillingAgent",
    [string]$ServiceName = "ServerManagerBillingWatchdog"
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE. Arguments: $($Arguments -join ' ')"
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "Please run this script as Administrator."
}

if (-not (Test-Path -LiteralPath $AgentExePath)) {
    throw "Agent executable not found: $AgentExePath"
}
if (-not (Test-Path -LiteralPath $ServiceExePath)) {
    throw "Service executable not found: $ServiceExePath"
}

Write-Host "Creating or updating scheduled task $TaskName ..."
Invoke-NativeCommand -FilePath "schtasks.exe" -Arguments @(
    "/Create",
    "/TN", $TaskName,
    "/TR", "`"$AgentExePath`"",
    "/SC", "ONLOGON",
    "/RL", "HIGHEST",
    "/F",
    "/IT"
)

Write-Host "Recreating service $ServiceName ..."
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

Invoke-NativeCommand -FilePath "sc.exe" -Arguments @(
    "create", $ServiceName,
    "binPath=", "`"$ServiceExePath`"",
    "start=", "auto"
)

Invoke-NativeCommand -FilePath "sc.exe" -Arguments @(
    "description", $ServiceName, "ServerManagerBilling watchdog service"
)

Write-Host "Starting service $ServiceName ..."
Start-Service -Name $ServiceName

Write-Host "Running scheduled task once ..."
Invoke-NativeCommand -FilePath "schtasks.exe" -Arguments @(
    "/Run",
    "/TN", $TaskName
)

Write-Host "Install completed."
