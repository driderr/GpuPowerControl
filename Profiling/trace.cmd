@echo off
REM ============================================================
REM Profiling/trace.cmd
REM CPU profiling via dotnet-trace (flame graph generation)
REM Output is displayed live AND saved to Profiling/results/
REM ============================================================
REM Usage: Profiling\trace.cmd [duration_seconds] [app_args...]
REM   duration_seconds: How long to collect trace data (default: 30)
REM   app_args: Optional arguments passed to the app after --
REM Example: Profiling\trace.cmd 60 --simulate --scenario Spike
REM ============================================================

setlocal

REM Ensure we're in the project root
cd /d "%~dp0.."

REM Parse arguments - first numeric arg is duration, rest go to the app
set DURATION=30
set APP_ARGS=
set FOUND_DURATION=0

:parse_args
if "%1"=="" goto :done_parsing
set TESTNUM=%1
for /f "delims=0123456789" %%A in ("%TESTNUM%") do set ISNUM=%%A
if "%ISNUM%"=="" (
    if "%FOUND_DURATION%"=="0" (
        set DURATION=%1
        set FOUND_DURATION=1
    ) else (
        if "%APP_ARGS%"=="" (
            set APP_ARGS=%1
        ) else (
            set APP_ARGS=%APP_ARGS% %1
        )
    )
) else if "%FOUND_DURATION%"=="0" (
    set DURATION=%1
    set FOUND_DURATION=1
)
shift
goto :parse_args
:done_parsing

REM Generate timestamp for output files
for /f "tokens=2 delims==" %%I in ('wmic OS Get localdatetime /value^|find "="') do set datetime=%%I
set TIMESTAMP=%datetime:~0,8%-%datetime:~8,6%

REM Setup results directory
if not exist "Profiling\results" mkdir "Profiling\results"
set TRACEFILE=Profiling\results\trace-%TIMESTAMP%.speedscope.json
set LOGFILE=Profiling\results\trace-%TIMESTAMP%.log

echo ==========================================================
echo  dotnet-trace profiling session
echo  Timestamp:  %TIMESTAMP%
echo  Duration:   %DURATION% seconds
echo  Trace file: %TRACEFILE%
echo  Log file:   %LOGFILE%
echo ==========================================================
echo.

REM Helper to write to log and display
echo [INFO] Duration: %DURATION% seconds > "%LOGFILE%"
echo [INFO] Trace output: %TRACEFILE% >> "%LOGFILE%"
echo [INFO] Started at: %datetime% >> "%LOGFILE%"
echo. >> "%LOGFILE%"

REM Check for required tools
where dotnet-trace >nul 2>&1
if %errorlevel% neq 0 (
    echo [INFO] Installing dotnet-trace tool...
    call dotnet tool update -g dotnet-trace >nul 2>&1
    if %errorlevel% neq 0 call dotnet tool install -g dotnet-trace >nul 2>&1
)
where dotnet-trace >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] dotnet-trace not found after install. Ensure .NET SDK is installed.
    pause
    exit /b 1
)

REM Build with profiling enabled (Release for accurate profiling)
echo [INFO] Building with profiling enabled (Release configuration)...
dotnet build -p:EnableProfiling=true --configuration Release --nologo -v q
if %errorlevel% neq 0 (
    echo [ERROR] Build failed. Fix errors and retry.
    pause
    exit /b 1
)
echo [OK] Build succeeded.
echo.

REM Run the app in the background with profiling enabled
echo [INFO] Starting app with --profile flag (Release)...
if "%APP_ARGS%"=="" (
    start /b dotnet run -p:EnableProfiling=true --configuration Release --no-build -- --profile --simulate
) else (
    start /b dotnet run -p:EnableProfiling=true --configuration Release --no-build -- --profile %APP_ARGS%
)

REM Give the app time to start
timeout /t 3 /nobreak >nul

REM Find the dotnet process using PowerShell for reliability
echo [INFO] Finding app process...
for /f "usebackq tokens=1" %%P in (`powershell -Command "(Get-CimInstance Win32_Process -Filter \"name='dotnet.exe'\" | Where-Object {$_.CommandLine -like '*--profile*'}).ProcessId"`) do (
    set APP_PID=%%P
)

if "%APP_PID%"=="" (
    echo [ERROR] Could not find the dotnet process with --profile flag.
    echo [INFO] List running dotnet processes: tasklist /FI "IMAGENAME eq dotnet.exe"
    pause
    exit /b 1
)

echo [OK] App PID: %APP_PID%
echo.
echo [INFO] Collecting trace for %DURATION% seconds...
echo [INFO] This will capture CPU profiling data for flame graph analysis.
echo [INFO] Trace will be saved to: %TRACEFILE%
echo.

echo [INFO] Running: dotnet-trace collect -p %APP_PID% --format speedscope --output "%TRACEFILE%" --delay 2 --duration %DURATION% >> "%LOGFILE%" 2>&1

REM Collect the trace
dotnet-trace collect -p %APP_PID% --format speedscope --output "%TRACEFILE%" --delay 2 --duration %DURATION% >> "%LOGFILE%" 2>&1

set TRACE_RESULT=%errorlevel%

echo.
echo [INFO] Trace collection finished (exit code: %TRACE_RESULT%).
echo.

REM Report results
if exist "%TRACEFILE%" (
    for %%S in ("%TRACEFILE%") do set FSIZE=%%~zS
    echo [OK] Trace file created: %TRACEFILE%
    echo [OK] File size: %FSIZE% bytes
    echo.
    echo [INFO] To view the flame graph:
    echo   1. Open https://speedscope.app
    echo   2. Drag ^& drop %TRACEFILE% into the browser
    echo.
    echo [INFO] To get analysis, paste the contents of %LOGFILE% to your assistant.
) else (
    echo [ERROR] Trace file was not created. Check %LOGFILE% for details.
)

echo.
echo ==========================================================
echo  Trace session complete.
echo  Trace: %TRACEFILE%
echo  Log:   %LOGFILE%
echo ==========================================================
pause