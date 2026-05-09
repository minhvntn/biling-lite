@echo off
title SERVER MANAGER BILLING - BACKEND SERVICE
color 0A
cd /d "%~dp0"

echo ========================================================
echo   KHOI DONG HE THONG QUAN LY PHONG GAME - BACKEND
echo ========================================================
echo.

:: 1. Check if Node.js is installed
where node >nul 2>nul
if %errorlevel% neq 0 (
    color 0C
    echo [ERROR] May cua ban chua cai dat Node.js!
    echo Vui long tai va cai dat Node.js tai: https://nodejs.org/
    echo.
    pause
    exit /b
)

:: 2. Check if node_modules exists, install if missing
if not exist "node_modules" (
    echo [INFO] Dang cai dat cac thu vien can thiet: npm install...
    call npm install
    if %errorlevel% neq 0 (
        color 0C
        echo [ERROR] Cai dat thu vien that bai!
        pause
        exit /b
    )
)

:: 3. Run Prisma DB Push to ensure Database schema is updated
echo [INFO] Dang dong bo cau hinh co so du lieu (Prisma db push)...
call npx prisma db push
if %errorlevel% neq 0 (
    echo [WARNING] Dong bo database gap canh bao/loi.
)

:: 4. Start NestJS Backend
echo [INFO] Dang khoi dong Backend Service...
echo May chu dang chay tai: http://localhost:9000
echo.
call npm run start:dev
pause
