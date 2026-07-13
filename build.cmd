@echo off
cls
echo =========================================
echo  SibangGenerator - Single EXE Build
echo =========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 goto nosdk

echo .NET SDK Version:
dotnet --version
echo.
echo [1/2] Restoring packages...
dotnet restore
if errorlevel 1 goto fail

echo.
echo [2/2] Publishing single file...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfContained=true -o bin\Release\net8.0-windows\win-x64\publish
if errorlevel 1 goto fail

echo.
echo =========================================
echo  SUCCESS!
echo =========================================
echo.

explorer bin\Release\net8.0-windows\win-x64\publish

pause
exit /b 0

:nosdk
echo [ERROR] .NET 8 SDK not found.
pause
exit /b 1

:fail
echo.
echo [FAIL] Build failed. Please check the error messages above.
pause
exit /b 1