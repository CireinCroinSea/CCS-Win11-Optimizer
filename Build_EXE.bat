@echo off
setlocal
title Build Win11 Gaming Optimizer

where dotnet >nul 2>nul
if errorlevel 1 goto no_sdk

dotnet --list-sdks
dotnet publish "%~dp0Win11GamingOptimizer.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto build_failed

echo.
echo Build completed.
echo EXE location:
echo %~dp0bin\Release\net8.0-windows\win-x64\publish\Win11GamingOptimizer.exe
echo.
pause
exit /b 0

:no_sdk
echo .NET SDK was not found.
echo Install .NET 8 SDK x64 from:
echo https://dotnet.microsoft.com/en-us/download/dotnet/8.0
echo Then close this window, open a new one, and run this file again.
echo.
pause
exit /b 1

:build_failed
echo.
echo Build failed. Read the error messages above.
echo Check that .NET 8 SDK is installed and internet access is available for package restore.
echo.
pause
exit /b 1
