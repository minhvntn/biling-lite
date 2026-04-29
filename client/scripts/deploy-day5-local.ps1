param(
    [string]$DistRoot = "i:\servermanagerbilling\client\dist\day5",
    [string]$InstallRoot = "C:\Program Files\ServerManagerBilling",
    [string]$TaskName = "ServerManagerBillingAgent",
    [string]$ServiceName = "ServerManagerBillingWatchdog"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "Please run this script as Administrator."
}

$agentSource = Join-Path $DistRoot "Agent"
$serviceSource = Join-Path $DistRoot "Service"
$installScript = Join-Path $DistRoot "install-day5.ps1"

if (-not (Test-Path $agentSource)) { throw "Missing agent dist folder: $agentSource" }
if (-not (Test-Path $serviceSource)) { throw "Missing service dist folder: $serviceSource" }
if (-not (Test-Path $installScript)) { throw "Missing install script: $installScript" }

$agentTarget = Join-Path $InstallRoot "Agent"
$serviceTarget = Join-Path $InstallRoot "Service"

Write-Host "Stopping existing service (if running) ..."
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host "Stopping running agent process (if any) ..."
Get-Process -Name "Client.Agent.Wpf" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Copying artifacts to $InstallRoot ..."
New-Item -ItemType Directory -Path $agentTarget -Force | Out-Null
New-Item -ItemType Directory -Path $serviceTarget -Force | Out-Null

Copy-Item -Path (Join-Path $agentSource "*") -Destination $agentTarget -Recurse -Force
Copy-Item -Path (Join-Path $serviceSource "*") -Destination $serviceTarget -Recurse -Force

$agentExe = Join-Path $agentTarget "Client.Agent.Wpf.exe"
$serviceExe = Join-Path $serviceTarget "Client.Watchdog.Service.exe"

if (-not (Test-Path $agentExe)) { throw "Agent executable missing after copy: $agentExe" }
if (-not (Test-Path $serviceExe)) { throw "Service executable missing after copy: $serviceExe" }

Write-Host "Installing scheduled task + watchdog service ..."
& $installScript -AgentExePath $agentExe -ServiceExePath $serviceExe -TaskName $TaskName -ServiceName $ServiceName

Write-Host "Deployment completed."
