@echo off
chcp 65001 >nul
echo ============================================
echo  Khoi dong Chatbot AI Tuyen sinh Dai hoc
echo ============================================

:: Kiem tra Docker dang chay
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Docker chua chay. Vui long khoi dong Docker Desktop roi thu lai.
    pause
    exit /b 1
)

echo [+] Docker OK. Dang build va khoi dong tat ca services...
echo.

docker compose up --build

echo.
echo ============================================
echo  Da khoi dong xong. Truy cap:
echo    - Frontend:  http://localhost:5173
echo    - Backend:   http://localhost:5000/health
echo    - AI Service: http://localhost:8000/health
echo    - Postgres:   localhost:5432
echo ============================================
echo.
echo Kiem tra trang thai cac container:
docker compose ps
