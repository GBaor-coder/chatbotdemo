@echo off
chcp 65001 >nul
echo ============================================
echo  Dung Chatbot AI Tuyen sinh Dai hoc
echo ============================================

docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Docker chua chay.
    pause
    exit /b 1
)

echo [+] Dang dung containers (giup giu volume pgdata de khoi phuc du lieu sau nay)...
docker compose down

echo.
echo [+] Da dung. Du lieu PostgreSQL van duoc luu trong volume pgdata.
echo    Neu muon xoa toan bo du lieu, chay: docker compose down -v

echo.
echo Kiem tra trang thai cac container:
docker compose ps
