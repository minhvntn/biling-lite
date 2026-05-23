param(
    [Parameter(Mandatory = $true)]
    [string]$Hostname,
    [string]$TunnelName = "servermanagerbilling-admin",
    [string]$LocalUrl = "http://127.0.0.1:5400",
    [string]$CloudflaredPath = "C:\Program Files (x86)\cloudflared\cloudflared.exe",
    [switch]$OverwriteDns
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Cloudflared {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Không tìm thấy cloudflared tại: $Path"
    }
}

function Ensure-CloudflaredLogin {
    param(
        [string]$Path,
        [string]$CloudflaredHome
    )

    $certPath = Join-Path $CloudflaredHome "cert.pem"
    if (Test-Path -LiteralPath $certPath) {
        return
    }

    Write-Host "Chưa đăng nhập Cloudflare. Mở trình duyệt để login..." -ForegroundColor Yellow
    & $Path tunnel login
    if ($LASTEXITCODE -ne 0) {
        throw "cloudflared tunnel login thất bại."
    }
}

function Get-TunnelByName {
    param(
        [string]$Path,
        [string]$Name
    )

    $json = & $Path tunnel list --output json
    if (-not $json) {
        return $null
    }

    $list = $json | ConvertFrom-Json
    return $list | Where-Object { $_.name -eq $Name } | Select-Object -First 1
}

Require-Cloudflared -Path $CloudflaredPath

$cloudflaredHome = Join-Path $env:USERPROFILE ".cloudflared"
if (-not (Test-Path -LiteralPath $cloudflaredHome)) {
    New-Item -Path $cloudflaredHome -ItemType Directory | Out-Null
}

Ensure-CloudflaredLogin -Path $CloudflaredPath -CloudflaredHome $cloudflaredHome

$tunnel = Get-TunnelByName -Path $CloudflaredPath -Name $TunnelName
if (-not $tunnel) {
    Write-Host "Tạo tunnel mới: $TunnelName" -ForegroundColor Cyan
    & $CloudflaredPath tunnel create $TunnelName
    if ($LASTEXITCODE -ne 0) {
        throw "Tạo tunnel thất bại."
    }
    $tunnel = Get-TunnelByName -Path $CloudflaredPath -Name $TunnelName
}

if (-not $tunnel) {
    throw "Không lấy được thông tin tunnel sau khi tạo."
}

$tunnelId = [string]$tunnel.id
$credentialsFile = Join-Path $cloudflaredHome "$tunnelId.json"
if (-not (Test-Path -LiteralPath $credentialsFile)) {
    throw "Không tìm thấy credentials file: $credentialsFile"
}

Write-Host "Map DNS $Hostname -> tunnel $TunnelName" -ForegroundColor Cyan
if ($OverwriteDns.IsPresent) {
    & $CloudflaredPath tunnel route dns --overwrite-dns $TunnelName $Hostname
}
else {
    & $CloudflaredPath tunnel route dns $TunnelName $Hostname
}
if ($LASTEXITCODE -ne 0) {
    throw "Route DNS thất bại."
}

$configPath = Join-Path $cloudflaredHome "config-servermanagerbilling.yml"
$yaml = @"
tunnel: $tunnelId
credentials-file: $credentialsFile

ingress:
  - hostname: $Hostname
    service: $LocalUrl
  - service: http_status:404
"@

Set-Content -Path $configPath -Value $yaml -Encoding UTF8

Write-Host ""
Write-Host "=========================================" -ForegroundColor Green
Write-Host "Setup tunnel hoàn tất"
Write-Host "Tunnel name: $TunnelName"
Write-Host "Hostname:    $Hostname"
Write-Host "Config:      $configPath"
Write-Host "=========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Lệnh chạy tunnel:"
Write-Host """$CloudflaredPath"" tunnel --config ""$configPath"" run $TunnelName"
Write-Host ""
Write-Host "Mẹo: backend + admin phải chạy trước."
