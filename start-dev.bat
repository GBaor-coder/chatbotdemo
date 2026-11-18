@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
cd /d "%~dp0"

:: ============================ Cau hinh ============================
set "PROJECT_NAME=demo-chatbot-ai"
set "WAIT_TIMEOUT=180"
set "WINDOW_TITLE_PREFIX=[DEV]"
set "DO_BUILD=1"
set "SHOW_LOGS=1"
set "BAD_ARG="

:: ============================ Xu ly tham so ============================
echo ============================================
echo  Khoi dong Chatbot AI Tuyen sinh Dai hoc
echo ============================================
:parse_args
if "%~1"=="" goto done_args
if /i "%~1"=="--help" goto show_help
if /i "%~1"=="-h" goto show_help
if /i "%~1"=="/?" goto show_help
if /i "%~1"=="--no-build" (set "DO_BUILD=0") else (
    if /i "%~1"=="--no-logs" (set "SHOW_LOGS=0") else (set "BAD_ARG=%~1")
)
shift
goto parse_args
:done_args
if defined BAD_ARG (
    echo [LOI] Tham so khong biet: %BAD_ARG%
    echo(
    goto show_help
)

:: ============================ Kiem tra Docker ============================
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Docker chua chay. Vui long mo Docker Desktop roi chay lai lenh nay.
    exit /b 1
)
echo [OK] Docker dang chay.

:: Lay danh sach service dong tu compose
set "SERVICES="
for /f "delims=" %%s in ('docker compose config --services 2^>nul') do set "SERVICES=!SERVICES! %%s"
if not defined SERVICES (
    echo [LOI] Khong lay duoc danh sach service tu docker-compose.yml.
    exit /b 1
)
echo [OK] Cac service:!SERVICES!

:: Kiem tra .env
if not exist ".env" (
    echo [WARN] Khong tim thay file .env. Mau: copy .env.example .env va dien LLM_API_KEY.
    echo    Stack co the khoi dong nhung goi LLM se that bai neu chua co key hop le.
    echo(
)

:: ============================ Khoi dong compose ============================
set "BUILD_FLAG="
if %DO_BUILD% equ 1 set "BUILD_FLAG=--build"

echo [+] Dang build va khoi dong he thong o che do nen (--remove-orphans bo container cu)...
docker compose up -d %BUILD_FLAG% --remove-orphans >nul 2>&1
set "UP_EXIT=%errorlevel%"
if %UP_EXIT% neq 0 (
    echo [!] docker compose that bai hoac qua thoi gian cho healthcheck. Xem log chi tiet ben duoi.
    echo(
    docker compose logs --tail=40
    exit /b %UP_EXIT%
)
echo [OK] Tat ca container da duoc khoi tao va healthy.
echo [+] Mo cua so log cho tung service (dong cua so KHONG tat service):
for %%s in (!SERVICES!) do (
    if %SHOW_LOGS% equ 1 (
        set "svc=%%s"
        set "port=?"
        if /i "%svc%"=="db" set "port=5432"
        if /i "%svc%"=="ai-service" set "port=8000"
        if /i "%svc%"=="backend-dotnet" set "port=5000"
        if /i "%svc%"=="frontend-react" set "port=5173"
        set "title=!WINDOW_TITLE_PREFIX! !svc! :!port!"
        tasklist /v /fo csv 2>nul | findstr /c:"!title!" >nul 2>&1
        if !errorlevel! equ 0 (
            echo [OK] Cua so log !svc! da bo qua.
        ) else (
            echo [+] Mo cua so: !title!
            start "" cmd /k "title !title! & docker compose logs -f !svc!"
        )
    )
)

:: ============================ Tom tat cuoi ============================
echo ============================================
echo  TOM TAT
echo ============================================
echo - Frontend (React):  http://localhost:5173
echo - Backend .NET (API): http://localhost:5000
echo   Health:            http://localhost:5000/health
echo - AI Service (Python): http://localhost:8000
echo   Health:            http://localhost:8000/api/v1/health
echo - PostgreSQL:        localhost:5432 (user: postgres, db: chatbot)
echo(
echo Thong tin demo (tu database/init.sql):
echo - Tenant mac dinh: 11111111-1111-1111-1111-111111111111 (Trung Dai hoc Mau A)
echo - Tai khoan DB:    postgres / postgres
echo(
echo Luu ý: Dong cua so log KHONG tat service. Neu muon tat he thong, chay: stop-dev.bat
echo(
echo [OK] He thong da san sang.
exit /b 0

:show_help
echo(
echo Su dung: start-dev.bat [--no-build] [--no-logs] [--help]
echo(
echo  --no-build   Khong build lai image (dung image da co).
echo  --no-logs    Khong mo cua so log tung service.
echo  --help       Tro giuong nay.
echo(
exit /b 0
