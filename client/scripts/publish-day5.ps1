param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$agentProject = Join-Path $root "src\Client.Agent.Wpf\Client.Agent.Wpf.csproj"
$serviceProject = Join-Path $root "src\Client.Watchdog.Service\Client.Watchdog.Service.csproj"

$distRoot = Join-Path $root "dist\day5"
$agentOut = Join-Path $distRoot "Agent"
$serviceOut = Join-Path $distRoot "Service"

Remove-Item -LiteralPath $distRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $agentOut -Force | Out-Null
New-Item -ItemType Directory -Path $serviceOut -Force | Out-Null

$selfContainedValue = if ($SelfContained.IsPresent) { "true" } else { "false" }

Write-Host "Publishing WPF Agent..."
dotnet publish $agentProject -c $Configuration -r $Runtime --self-contained $selfContainedValue -o $agentOut

Write-Host "Publishing Watchdog Service..."
dotnet publish $serviceProject -c $Configuration -r $Runtime --self-contained $selfContainedValue -o $serviceOut

Copy-Item -Path (Join-Path $PSScriptRoot "install-day5.ps1") -Destination (Join-Path $distRoot "install-day5.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "uninstall-day5.ps1") -Destination (Join-Path $distRoot "uninstall-day5.ps1") -Force

Write-Host "Publish complete: $distRoot"
Write-Host "Agent output: $agentOut"
Write-Host "Service output: $serviceOut"
