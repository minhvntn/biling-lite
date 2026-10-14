@echo off
chcp 65001 >nul
title CẤU HÌNH IP MÁY CHỦ CHO MÁY TRẠM (CLIENT)
color 0E
cd /d "%~dp0"

echo =======================================================================
echo        CẤU HÌNH ĐỊA CHỈ IP MÁY CHỦ (SERVER MANAGER BILLING)
echo =======================================================================
echo.
echo Nhập địa chỉ IP của Máy Chủ (Server) trong mạng LAN.
echo (Ví dụ: 192.168.1.100 hoặc 192.168.1.200 hoặc localhost)
echo.
set /p SERVER_IP="👉 Nhập IP Máy Chủ: "

if "%SERVER_IP%"=="" (
    echo [LỖI] Bạn chưa nhập địa chỉ IP!
    pause
    exit /b 1
)

:: Tạo lại file appsettings.json với IP mới
(
echo {
echo   "Agent": {
echo     "ServerUrl": "http://%SERVER_IP%:39090",
echo     "AgentId": "",
echo     "HeartbeatIntervalSeconds": 10,
echo     "EnableAutoStartup": true,
echo     "TotalSessionMinutes": 60000,
echo     "HourlyRate": 12000
echo   }
echo }
) > "%~dp0Client-Agent\appsettings.json"

echo.
echo =======================================================================
echo ✅ ĐÃ LƯU CẤU HÌNH THÀNH CÔNG!
echo 👉 Địa chỉ kết nối: http://%SERVER_IP%:39090
echo =======================================================================
echo.
echo Bây giờ bạn có thể chạy file "2-Chay-Client.bat" để mở ứng dụng máy trạm.
echo.
pause
