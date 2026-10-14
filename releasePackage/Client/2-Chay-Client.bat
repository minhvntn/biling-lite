@echo off
chcp 65001 >nul
title KHỞI CHẠY MÁY TRẠM (CLIENT AGENT)
color 0A
cd /d "%~dp0Client-Agent"

if exist "Client.Agent.Wpf.exe" (
    start "" "Client.Agent.Wpf.exe"
) else (
    echo [LỖI] Không tìm thấy file Client.Agent.Wpf.exe trong thư mục Client-Agent!
    pause
    exit /b 1
)

exit
