@echo off
cd /d "%~dp0"
dotnet run --project "quick-product\EPB_UsbTest\EPB_UsbTest.csproj" -c Debug
pause
