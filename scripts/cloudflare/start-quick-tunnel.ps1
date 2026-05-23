param(
    [string]$LocalUrl = "http://127.0.0.1:5400",
    [string]$CloudflaredPath = "C:\Program Files (x86)\cloudflared\cloudflared.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CloudflaredPath)) {
    throw "Không tìm thấy cloudflared tại: $CloudflaredPath"
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Cloudflare Quick Tunnel (không cần domain)" -ForegroundColor Cyan
Write-Host "Local URL: $LocalUrl"
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Lưu ý:"
Write-Host "1) Hãy chạy backend (9000) + admin dev (5400) trước."
Write-Host "2) URL public sẽ là dạng https://*.trycloudflare.com và thay đổi mỗi lần chạy."
Write-Host ""

& $CloudflaredPath tunnel --url $LocalUrl --no-autoupdate
