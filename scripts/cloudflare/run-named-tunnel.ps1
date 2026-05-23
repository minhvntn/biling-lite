param(
    [string]$TunnelName = "servermanagerbilling-admin",
    [string]$CloudflaredPath = "C:\Program Files (x86)\cloudflared\cloudflared.exe",
    [string]$ConfigPath = "$env:USERPROFILE\.cloudflared\config-servermanagerbilling.yml"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CloudflaredPath)) {
    throw "Không tìm thấy cloudflared tại: $CloudflaredPath"
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "Không tìm thấy config tunnel tại: $ConfigPath"
}

& $CloudflaredPath tunnel --config $ConfigPath run $TunnelName
