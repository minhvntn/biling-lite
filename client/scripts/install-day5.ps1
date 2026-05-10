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

function Show-ServiceTroubleshooting {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceNameValue,
        [Parameter(Mandatory = $true)]
        [string]$ServiceExePathValue
    )

    Write-Warning "Watchdog service failed to start. Collecting quick diagnostics..."

    $serviceDir = Split-Path -Parent $ServiceExePathValue
    $hostFxrPath = Join-Path $serviceDir "hostfxr.dll"
    if (-not (Test-Path -LiteralPath $hostFxrPath)) {
        Write-Warning "No hostfxr.dll next to watchdog exe. This build may be framework-dependent."
        Write-Warning "Use client\\scripts\\publish-day5.ps1 without -FrameworkDependent to publish self-contained."
    }

    try {
        $scmEvents = Get-WinEvent -FilterHashtable @{
            LogName = "System"
            ProviderName = "Service Control Manager"
            StartTime = (Get-Date).AddMinutes(-10)
        } -MaxEvents 30 |
            Where-Object { $_.Message -like "*$ServiceNameValue*" } |
            Select-Object -First 3

        foreach ($evt in $scmEvents) {
            $msg = ($evt.Message -replace "\r?\n", " ").Trim()
            Write-Host "SCM Event [$($evt.Id)] $($evt.TimeCreated): $msg"
        }
    }
    catch {
        Write-Warning "Could not read Service Control Manager events: $($_.Exception.Message)"
    }

    try {
        $dotnetEvents = Get-WinEvent -FilterHashtable @{
            LogName = "Application"
            ProviderName = ".NET Runtime"
            StartTime = (Get-Date).AddMinutes(-10)
        } -MaxEvents 30 |
            Where-Object { $_.Message -like "*Client.Watchdog.Service*" } |
            Select-Object -First 3

        foreach ($evt in $dotnetEvents) {
            $msg = ($evt.Message -replace "\r?\n", " ").Trim()
            Write-Host ".NET Runtime Event [$($evt.Id)] $($evt.TimeCreated): $msg"
        }
    }
    catch {
        Write-Warning "Could not read .NET Runtime events: $($_.Exception.Message)"
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
try {
    Start-Service -Name $ServiceName -ErrorAction Stop
    Start-Sleep -Seconds 1
    $status = (Get-Service -Name $ServiceName -ErrorAction Stop).Status
    if ($status -ne "Running") {
        throw "Service state after start: $status"
    }
}
catch {
    Show-ServiceTroubleshooting -ServiceNameValue $ServiceName -ServiceExePathValue $ServiceExePath
    throw
}

Write-Host "Running scheduled task once ..."
Invoke-NativeCommand -FilePath "schtasks.exe" -Arguments @(
    "/Run",
    "/TN", $TaskName
)

Write-Host "Install completed."
