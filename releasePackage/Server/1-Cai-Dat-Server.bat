@echo off
chcp 65001 >nul
title CÀI ĐẶT MÁY CHỦ (SERVER MANAGER BILLING)
color 0A
cd /d "%~dp0Backend"

echo =======================================================================
echo        CÀI ĐẶT VÀ KHỞI TẠO MÁY CHỦ - SERVER MANAGER BILLING
echo =======================================================================
echo.

:: 1. Kiểm tra Node.js
where node >nul 2>nul
if %errorlevel% neq 0 (
    color 0C
    echo [LỖI] Máy tính của bạn chưa cài đặt Node.js!
    echo Vui lòng tải và cài đặt Node.js (phiên bản LTS khuyến nghị) tại:
    echo 👉 https://nodejs.org/
    echo.
    echo Sau khi cài đặt Node.js xong, vui lòng chạy lại file này.
    echo.
    pause
    exit /b 1
)

echo [1/3] Kiểm tra môi trường Node.js: ĐÃ SẴN SÀNG!
echo.

:: 2. Cài đặt thư viện Backend
echo [2/3] Đang cài đặt thư viện cần thiết (npm install)...
call npm install --omit=dev
if %errorlevel% neq 0 (
    color 0C
    echo [LỖI] Cài đặt thư viện thất bại! Vui lòng kiểm tra kết nối mạng Internet.
    pause
    exit /b 1
)
echo Cài đặt thư viện thành công!
echo.

:: 3. Khởi tạo Database Prisma
echo [3/3] Đang khởi tạo và đồng bộ Cơ sở dữ liệu (Database)...
call npx prisma generate
call npx prisma db push
if %errorlevel% neq 0 (
    echo [CẢNH BÁO] Đồng bộ database có cảnh báo, tiếp tục hoàn tất...
)
echo.

echo =======================================================================
echo     🎉 CÀI ĐẶT MÁY CHỦ THÀNH CÔNG RỰC RỠ!
echo =======================================================================
echo.
echo Bây giờ bạn có thể:
echo 👉 Chạy file "2-Khoi-Dong-Server.bat" để mở Hệ thống Quản lý và Máy chủ.
echo.
pause
