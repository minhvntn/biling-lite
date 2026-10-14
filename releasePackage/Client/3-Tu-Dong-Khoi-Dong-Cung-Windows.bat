@echo off
chcp 65001 >nul
title TỰ ĐỘNG KHỞI ĐỘNG CÙNG WINDOWS - CLIENT AGENT
color 0B
cd /d "%~dp0"

echo =======================================================================
echo        CÀI ĐẶT TỰ ĐỘNG KHỞI ĐỘNG CÙNG WINDOWS CHO MÁY TRẠM
echo =======================================================================
echo.
echo 1. Kích hoạt tự động khởi động cùng Windows
echo 2. Hủy tự động khởi động cùng Windows
echo.
set /p CHOICE="👉 Nhập lựa chọn của bạn (1 hoặc 2): "

set "TARGET_EXE=%~dp0Client-Agent\Client.Agent.Wpf.exe"
set "STARTUP_FOLDER=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "SHORTCUT_PATH=%STARTUP_FOLDER%\ClientAgentBilling.lnk"

if "%CHOICE%"=="1" (
    powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%SHORTCUT_PATH%'); $s.TargetPath = '%TARGET_EXE%'; $s.WorkingDirectory = '%~dp0Client-Agent'; $s.Save()"
    echo.
    echo ✅ ĐÃ KÍCH HOẠT: Client Agent sẽ tự động chạy mỗi khi máy tính khởi động!
) else if "%CHOICE%"=="2" (
    if exist "%SHORTCUT_PATH%" del /f /q "%SHORTCUT_PATH%"
    echo.
    echo ✅ ĐÃ HỦY: Đã xóa shortcut khởi động cùng Windows.
) else (
    echo [LỖI] Lựa chọn không hợp lệ!
)

echo.
pause
