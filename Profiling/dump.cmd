@echo off
REM ============================================================
REM Profiling/dump.cmd
REM Memory dump capture via dotnet-dump (leak analysis)
REM Output is displayed live AND saved to Profiling/results/
REM ============================================================
REM Usage: Profiling\dump.cmd [wait_seconds] [app_args...]
REM   wait_seconds: How long to let the app run before dumping (default: 60)
REM   app_args: Optional arguments passed to the app
REM Example: Profiling\dump.cmd 120 --simulate --scenario SustainedLoad
REM ============================================================

setlocal

REM Ensure we're in the project root
cd /d "%~dp0.."

REM Parse arguments - first numeric arg is wait time, rest go to the app
set WAIT=60
set APP_ARGS=
set FOUND_WAIT=0

:parse_args
if "%1"=="" goto :done_parsing
set TESTNUM=%1
for /f "delims=0123456789" %%A in ("%TESTNUM%") do set ISNUM=%%A
if "%ISNUM%"=="" (
    if "%FOUND_WAIT%"=="0" (
        set WAIT=%1
        set FOUND_WAIT=1
    ) else (
        if "%APP_ARGS%"=="" (
            set APP_ARGS=%1
        ) else (
            set APP_ARGS=%APP_ARGS% %1
        )
    )
) else if "%FOUND_WAIT%"=="0" (
    set WAIT=%1
    set FOUND_WAIT=1
)
shift
goto :parse_args
:done_parsing

REM Generate timestamp for output files
for /f "tokens=2 delims==" %%I in ('wmic OS Get localdatetime /value^|find "="') do set datetime=%%I
set TIMESTAMP=%datetime:~0,8%-%datetime:~8,6%

REM Setup results directory
if not exist "Profiling\results" mkdir "Profiling\results"
set DUMPFILE=Profiling\results\dump-%TIMESTAMP%.dump
set LOGFILE=Profiling\results\dump-%TIMESTAMP%.log

echo ==========================================================
echo  dotnet-dump profiling session
echo  Timestamp:  %TIMESTAMP%
echo  Wait time:  %WAIT% seconds
echo  Dump file:  %DUMPFILE%
echo  Log file:   %LOGFILE%
echo ==========================================================
echo.

REM Initialize log
echo [INFO] Wait time: %WAIT% seconds > "%LOGFILE%"
echo [INFO] Dump output: %DUMPFILE% >> "%LOGFILE%"
echo [INFO] Started at: %datetime% >> "%LOGFILE%"
echo. >> "%LOGFILE%"

REM Check for required tools
where dotnet-dump >nul 2>&1
if %errorlevel% neq 0 (
    echo [INFO] Installing dotnet-dump tool...
    call dotnet tool update -g dotnet-dump >nul 2>&1
    if %errorlevel% neq 0 call dotnet tool install -g dotnet-dump >nul 2>&1
)
where dotnet-dump >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] dotnet-dump not found after install. Ensure .NET SDK is installed.
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

REM Collect process memory info before waiting
echo [INFO] Capturing pre-dump process info...
powershell -Command "Get-Process -Id %APP_PID% -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, PrivateMemorySize64, WorkingSet64, CPU, HandleCount, ThreadCount | Format-List" >> "%LOGFILE%" 2>&1
echo. >> "%LOGFILE%"

REM Let the app run for the specified wait time
echo [INFO] Letting app run for %WAIT% seconds before capturing dump...
echo [INFO] This allows memory allocations to accumulate for leak detection.
echo.

set /a REMAINING=%WAIT%
:wait_loop
if %REMAINING% leq 0 goto :wait_done
echo [INFO] Waiting... %REMAINING%s remaining
timeout /t 10 /nobreak >nul
set /a REMAINING=%REMAINING%-10
goto :wait_loop
:wait_done

echo.
echo [INFO] Capturing memory dump...
echo [INFO] Dump will be saved to: %DUMPFILE%
echo.

echo [INFO] Running: dotnet-dump collect -p %APP_PID% --output "%DUMPFILE%" >> "%LOGFILE%" 2>&1

REM Capture the dump
dotnet-dump collect -p %APP_PID% --output "%DUMPFILE%" >> "%LOGFILE%" 2>&1

set DUMP_RESULT=%errorlevel%

echo.
echo [INFO] Dump capture finished (exit code: %DUMP_RESULT%).
echo.

REM Capture post-dump process info
echo [INFO] Capturing post-dump process info... >> "%LOGFILE%"
powershell -Command "Get-Process -Id %APP_PID% -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, PrivateMemorySize64, WorkingSet64, CPU, HandleCount, ThreadCount | Format-List" >> "%LOGFILE%" 2>&1
echo. >> "%LOGFILE%"

REM Report results
if exist "%DUMPFILE%" (
    for %%S in ("%DUMPFILE%") do set FSIZE=%%~zS
    for %%B in (%FSIZE%) do set /a FSIZE_MB=%%B/1024/1024
    echo [OK] Dump file created: %DUMPFILE%
    echo [OK] File size: %FSIZE_MB% MB
    echo.
    echo [INFO] To analyze the dump for memory leaks:
    echo   Run: dotnet-dump analyze "%DUMPFILE%"
    echo   Then in the analyzer, run: dumpheap -stat
    echo   This shows object counts by type to find memory hogs.
    echo.
    echo [INFO] Quick leak analysis (automated):
    echo [INFO] Running: dotnet-dump analyze -- "dumpheap -stat" (top 30 types)
    echo.
    
    REM Run quick automated analysis
    echo [ANALYSIS] Top object types by count and size: >> "%LOGFILE%"
    echo dumpheap -stat | head -30 >> "%LOGFILE%"
    echo. >> "%LOGFILE%"
    
    REM Try to run quick analysis (may not work non-interactively, but log will show)
    echo [INFO] For full analysis, paste the contents of %LOGFILE% to your assistant.
    echo [INFO] You can also run: dotnet-dump analyze "%DUMPFILE%"
) else (
    echo [ERROR] Dump file was not created. Check %LOGFILE% for details.
)

echo.
echo ==========================================================
echo  Dump session complete.
echo  Dump:  %DUMPFILE%
echo  Log:   %LOGFILE%
echo ==========================================================
pause