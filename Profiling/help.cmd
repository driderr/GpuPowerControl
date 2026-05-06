@echo off
REM ============================================================
REM Profiling/help.cmd
REM Quick reference for all profiling scripts
REM ============================================================

echo.
echo ==========================================================
echo  GpuPowerControl - Profiling Tools
echo ==========================================================
echo.
echo  Overview:
echo  ---------
echo  These scripts profile CPU and memory usage of the
echo  GpuPowerControl application. Each script:
echo    - Builds the project with profiling enabled
echo    - Runs the app with --profile instrumentation
echo    - Shows output LIVE in the terminal
echo    - Saves results to Profiling/results/ for later analysis
echo.
echo  Scripts:
echo  --------
echo.
echo  1. counters.cmd [app_args...]
echo     Tool:     dotnet-counters
echo     Purpose:  Live monitoring of CPU%, memory, GC stats
echo     Output:   counters-{timestamp}.log
echo     Usage:    Profiling\counters.cmd
echo               Profiling\counters.cmd --simulate --scenario Spike
echo     Stops:    Press Ctrl+C anytime
echo.
echo  2. trace.cmd [duration] [app_args...]
echo     Tool:     dotnet-trace
echo     Purpose:  CPU flame graph (find hot methods)
echo     Output:   trace-{timestamp}.speedscope.json  (view at speedscope.app)
echo               trace-{timestamp}.log
echo     Usage:    Profiling\trace.cmd              (30s default)
echo               Profiling\trace.cmd 60           (60 seconds)
echo               Profiling\trace.cmd 45 --simulate --scenario Idle
echo.
echo  3. dump.cmd [wait_seconds] [app_args...]
echo     Tool:     dotnet-dump
echo     Purpose:  Memory dump for leak analysis
echo     Output:   dump-{timestamp}.dump  (analyze with: dotnet-dump analyze file.dump)
echo               dump-{timestamp}.log
echo     Usage:    Profiling\dump.cmd               (60s default)
echo               Profiling\dump.cmd 120           (wait 120s before dump)
echo               Profiling\dump.cmd 90 --simulate --scenario SustainedLoad
echo.
echo  Results Directory:
echo  ------------------
echo    Profiling\results\
echo      counters-*.log           - Counter readings over time
echo      trace-*.speedscope.json  - Flame graph data (open in browser)
echo      trace-*.log              - Trace session metadata
echo      dump-*.dump              - Memory dumps (~10-100MB each)
echo      dump-*.log               - Dump session metadata
echo.
echo  Prerequisites:
echo  --------------
echo    - .NET SDK 10.0 (includes dotnet CLI)
echo    - First run of each script will auto-install the required tool:
echo        counters.cmd -> dotnet-counters
echo        trace.cmd    -> dotnet-trace
echo        dump.cmd     -> dotnet-dump
echo.
echo  Analyzing Results:
echo  ------------------
echo    1. Counters log:
echo       Look for: steadily increasing "Gen 2 Heap Size" (memory leak),
echo       high "CPU %" sustained (CPU hog), frequent "Gen 2 GC" (pressure)
echo.
echo    2. Trace (flame graph):
echo       Open trace-*.speedscope.json in https://speedscope.app
echo       Widest bars = most CPU time spent in that method
echo.
echo    3. Memory dump:
echo       Run: dotnet-dump analyze Profiling\results\dump-*.dump
echo       Then: dumpheap -stat
echo       Look for: unexpectedly high object counts of specific types
echo.
echo  Giving Results to Your Assistant:
echo  ----------------------------------
echo    Copy and paste the contents of the .log files for analysis.
echo    For flame graphs, describe the widest bars you see.
echo    For dumps, paste the "dumpheap -stat" output.
echo.
echo  Build Note:
echo  -----------
echo    Profiling is toggleable. Normal builds have zero profiling overhead.
echo    These scripts automatically build with -p:EnableProfiling=true.
echo    Manual: dotnet build -p:EnableProfiling=true
echo.
echo ==========================================================
pause