@echo off
chcp 65001 >nul
title KHỞI ĐỘNG HỆ THỐNG MÁY CHỦ (SERVER MANAGER BILLING)
color 0B
cd /d "%~dp0"

echo =======================================================================
echo        KHỞI ĐỘNG HỆ THỐNG MÁY CHỦ (SERVER & QUẢN TRỊ ADMIN)
echo =======================================================================
echo.

:: 1. Khởi chạy Backend Service trong cửa sổ riêng
echo [1/2] Đang khởi chạy Backend Service (Cổng: 9000)...
start "SERVER MANAGER BILLING - BACKEND SERVICE" /min cmd /c "cd /d "%~dp0Backend" && node dist/src/main.js"

:: 2. Đợi 2 giây cho Backend sẵn sàng
timeout /t 2 /nobreak >nul

:: 3. Khởi chạy Server Admin App
echo [2/2] Đang mở Ứng dụng Quản lý Máy chủ (Server Admin App)...
if exist "%~dp0Server-Admin-App\Server.Admin.App.exe" (
    start "" "%~dp0Server-Admin-App\Server.Admin.App.exe"
) else (
    echo [LỖI] Không tìm thấy file Server.Admin.App.exe trong thư mục Server-Admin-App!
    pause
    exit /b 1
)

echo.
echo ✅ Hệ thống Máy chủ và Ứng dụng Quản trị đã được khởi động!
echo 👉 Vui lòng không tắt cửa sổ Backend Service khi đang vận hành quán.
echo.
timeout /t 3 >nul
exit
