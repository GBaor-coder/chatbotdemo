@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "CLEAN=0"
set "BAD_ARG="
:parse_args
if "%~1"=="" goto done_args
if /i "%~1"=="--help" goto show_help
if /i "%~1"=="--clean" (set "CLEAN=1") else (set "BAD_ARG=%~1")
shift
goto parse_args
:done_args
if defined BAD_ARG (
    echo [LOI] Tham so khong biet: %BAD_ARG%
    goto show_help
)
echo ============================================
echo  Dung Chatbot AI Tuyen sinh Dai hoc
echo ============================================
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Docker chua chay.
    exit /b 1
)
echo [OK] Docker dang chay.
if "%CLEAN%"=="1" goto confirm_clean
goto skip_clean
:confirm_clean
echo [!] CANH BAO: --clean se XOA toan bo du lieu PostgreSQL (volume pgdata).
echo    Hanh dong nay KHONG the hoan tac.
set /p CONFIRM="Ban co chac chan muon xoa toan bo du lieu? nhap co de xac nhan: "
if /i not "%CONFIRM%"=="y" (
    echo Da huy bo xoa du lieu. Chi dung container, giu nguyen du lieu.
    set "CLEAN=0"
)
:skip_clean
echo [+] Dang dung he thong...
if "%CLEAN%"=="1" (
    docker compose down -v --remove-orphans >nul 2>&1
) else (
    docker compose down --remove-orphans >nul 2>&1
)
if errorlevel 1 (
    echo [!] docker compose down that bai.
    exit /b 1
)
echo [OK] Da dung he thong thanh cong.
if "%CLEAN%"=="1" goto clean_msg
echo [OK] Du lieu PostgreSQL van con luu trong volume pgdata.
goto done
:clean_msg
echo [OK] Da xoa toan bo du lieu (volume pgdata da bi xoa).
:done
echo    Neu muon bo tat toan bo, chay lai: stop-dev.bat
exit /b 0
:show_help
echo Su dung: stop-dev.bat [--clean] [--help]
echo  --clean    Xoa toan bo du lieu PostgreSQL (hoi xac nhan truoc).
echo  --help     Tro giuong nay.
exit /b 0
