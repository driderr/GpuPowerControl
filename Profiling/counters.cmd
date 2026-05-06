@echo off
REM ============================================================
REM Profiling/counters.cmd
REM CPU, memory, and GC monitoring via dotnet-counters collect
REM ============================================================
REM Usage: Profiling\counters.cmd [duration_minutes] [app_args...]
REM   duration_minutes: Optional duration in minutes (default: 5)
REM   app_args: Optional arguments passed to the app (e.g., --simulate --scenario Spike)
REM
REM Behavior:
REM   - App opens in a separate console window
REM   - dotnet-counters collect runs in this window, writing JSON to file
REM   - Press 'q' in the app window to stop collection early
REM   - If the timer expires first, the app is automatically terminated
REM   - Data is collected continuously at 2-second intervals
REM   - Output is a time series with timestamped samples
REM ============================================================

setlocal

REM Ensure we're in the project root
cd /d "%~dp0.."

REM Parse optional duration argument (default: 5 minutes)
set DURATION_MIN=5
if not "%~1"=="" (
    REM Check if first argument looks like a number (duration)
    echo %~1|findstr /R "^[0-9][0-9]*$">nul
    if not errorlevel 1 (
        set DURATION_MIN=%~1
        shift
    )
)

REM Convert minutes to duration format (00:MM:00)
if %DURATION_MIN% LSS 10 (
    set DURATION=00:0%DURATION_MIN%:00
) else (
    set DURATION=00:%DURATION_MIN%:00
)

REM Collect remaining arguments as app args
set APP_ARGS=%*

REM Generate timestamp for output files
for /f "tokens=2 delims==" %%I in ('wmic OS Get localdatetime /value^|find "="') do set datetime=%%I
set TIMESTAMP=%datetime:~0,8%-%datetime:~8,6%

REM Setup results directory
if not exist "Profiling\results" mkdir "Profiling\results"
set LOGFILE=Profiling\results\counters-%TIMESTAMP%.json

echo ==========================================================
echo  dotnet-counters profiling session
echo  Timestamp:    %TIMESTAMP%
echo  Duration:     %DURATION%
echo  Output:       %LOGFILE%
echo  App args:     %APP_ARGS%
echo ==========================================================
echo.

REM Check for required tools
where dotnet-counters >nul 2>&1
if %errorlevel% neq 0 (
    echo [INFO] Installing dotnet-counters tool...
    call dotnet tool update -g dotnet-counters >nul 2>&1
    if %errorlevel% neq 0 call dotnet tool install -g dotnet-counters >nul 2>&1
)
where dotnet-counters >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] dotnet-counters not found after install. Ensure .NET SDK is installed.
    pause
    exit /b 1
)

REM Build (no special flags needed)
echo [INFO] Building (Release configuration)...
dotnet build --configuration Release --nologo -v q
if %errorlevel% neq 0 (
    echo [ERROR] Build failed. Fix errors and retry.
    pause
    exit /b 1
)
echo [OK] Build succeeded.
echo.

REM Start app in separate window
echo [INFO] Starting app in separate window...
echo [INFO] Press 'q' in the app console to stop collection early.
echo.

start "" dotnet exec bin\Release\net10.0-windows\GpuPowerControl.dll %APP_ARGS%

REM Wait for app to start, then use 'dotnet-counters ps' to find it
echo [INFO] Waiting for app to appear in dotnet-counters ps...
set APP_PID=
for /l %%i in (1,1,30) do (
    for /f "tokens=1" %%P in ('dotnet-counters ps 2^>nul ^| findstr /I "GpuPowerControl"') do (
        if not "%%P"=="" (
            set APP_PID=%%P
            goto pid_found
        )
    )
    timeout /t 1 /nobreak >nul
)
echo [ERROR] Timed out waiting for app to appear.
pause
exit /b 1

:pid_found
echo [OK] Found app process (PID: %APP_PID%)

REM Small delay to let runtime fully initialize EventPipe
timeout /t 2 /nobreak >nul

REM Run dotnet-counters collect (blocks until duration expires or process exits)
REM Data is written to the JSON file continuously at each refresh interval
echo [INFO] Collecting counters for up to %DURATION%...
echo [INFO] Output file: %LOGFILE%
echo [INFO] Press 'q' in the app window to stop early.
echo.

dotnet-counters collect -p %APP_PID% --format json -o "%LOGFILE%" --refresh-interval 2 --duration %DURATION% --counters System.Runtime

REM Collection is done - check if app is still running and clean up
echo.
echo [INFO] Collection complete.
for /f "tokens=1" %%P in ('tasklist /FI "PID eq %APP_PID%" /NH 2^>nul ^| findstr /I "GpuPowerControl"') do (
    if not "%%P"=="" (
        echo [INFO] App is still running - terminating process %APP_PID%...
        taskkill /PID %APP_PID% /T /F >nul 2>&1
        echo [OK] Process terminated.
    )
)

echo.
echo ==========================================================
echo  Profiling session complete.
echo  Output file: %LOGFILE%
echo ==========================================================
echo.
echo [INFO] To analyze: open %LOGFILE% in a text editor or paste its contents to your assistant.
pause