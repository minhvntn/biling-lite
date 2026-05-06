param(
    [Parameter(Mandatory = $true)]
    [string]$ServerUrl,
    [Parameter(Mandatory = $true)]
    [string]$AgentId,
    [string]$DistRoot = "i:\servermanagerbilling\client\dist\day5"
)

$ErrorActionPreference = "Stop"

$agentSettingsPath = Join-Path $DistRoot "Agent\appsettings.json"
$serviceSettingsPath = Join-Path $DistRoot "Service\appsettings.json"

if (-not (Test-Path $agentSettingsPath)) {
    throw "Agent appsettings not found: $agentSettingsPath"
}
if (-not (Test-Path $serviceSettingsPath)) {
    throw "Service appsettings not found: $serviceSettingsPath"
}

$agentJson = Get-Content -Path $agentSettingsPath -Raw | ConvertFrom-Json
$agentJson.Agent.ServerUrl = $ServerUrl
$agentJson.Agent.AgentId = $AgentId
$agentJson | ConvertTo-Json -Depth 10 | Set-Content -Path $agentSettingsPath -Encoding UTF8

$serviceJson = Get-Content -Path $serviceSettingsPath -Raw | ConvertFrom-Json
$serviceJson.Watchdog.AgentProcessName = "Client.Agent.Wpf"
$serviceJson.Watchdog.AgentExecutablePath = "C:\Program Files\ServerManagerBilling\Agent\Client.Agent.Wpf.exe"
$serviceJson.Watchdog.ScheduledTaskName = "ServerManagerBillingAgent"
$serviceJson.Watchdog.CheckIntervalSeconds = 2
$serviceJson.Watchdog.RestartCooldownSeconds = 5
$serviceJson | ConvertTo-Json -Depth 10 | Set-Content -Path $serviceSettingsPath -Encoding UTF8

Write-Host "Configured day5 package successfully."
Write-Host "ServerUrl=$ServerUrl"
Write-Host "AgentId=$AgentId"
