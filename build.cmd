@echo off
chcp 65001 >nul
echo.
echo  시방 생성 — 단일 exe 빌드
echo  ─────────────────────────────
echo.
where dotnet >nul 2>&1
if errorlevel 1 goto :nosdk
dotnet --list-sdks >nul 2>&1
if errorlevel 1 goto :nosdk

echo  SDK 버전:
dotnet --version
echo.
echo  [1/2] 패키지 복원...
dotnet restore || goto :fail
echo.
echo  [2/2] 단일 파일 게시...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true || goto :fail
echo.
echo  ─────────────────────────────
echo  완료.
echo  bin\Release\net8.0-windows\win-x64\publish\SibangGenerator.exe
echo.
pause
exit /b 0

:nosdk
echo  [오류] .NET 8 SDK를 찾을 수 없습니다.
echo.
echo  런타임만 설치된 경우에도 이 메시지가 나옵니다.
echo  https://dotnet.microsoft.com/download/dotnet/8.0 에서
echo  SDK ^(Runtime 아님^) 를 설치한 뒤 새 창에서 다시 실행하세요.
echo.
pause
exit /b 1

:fail
echo.
echo  [실패] 위 오류 메시지를 확인하세요.
echo  NuGet 접근이 막혀 있으면 사내 미러를 nuget.config 에 지정해야 합니다.
pause
exit /b 1
