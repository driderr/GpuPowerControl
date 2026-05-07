@echo off
echo ================================================
echo Building GpuPowerControl as single-file exe...
echo ================================================

dotnet publish -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish

if %errorlevel% == 0 (
    echo.
    echo Done! Your executable is at: .\publish\GpuPowerControl.exe
    echo.
    echo You can now copy GpuPowerControl.exe to your Desktop and run it.
    echo Note: Run as Administrator for GPU control features.
) else (
    echo.
    echo BUILD FAILED with error code %errorlevel%
)