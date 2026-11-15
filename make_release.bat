@echo off
setlocal
REM ===== FUT REBORN - build (works from ANY folder / drive, any folder name) =====
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 ( echo ERROR: .NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 & goto :fail )

if exist Release rmdir /s /q Release
mkdir Release\Server

echo [1/4] Building server (single exe)...
dotnet publish "Server\LocalFUT16.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "Release\Server" || goto :fail

echo [2/4] Copying server data files...
robocopy "Server" "Release\Server" /E /XD bin obj .vs save certs /XF *.cs *.csproj *.bak* *.rar *.zip *.md files.txt /NFL /NDL /NJH /NJS >nul
if errorlevel 8 goto :fail

echo [3/4] Copying certificates...
if not exist "Server\certs\leaf.pfx" ( echo ERROR: Server\certs\leaf.pfx is missing & goto :fail )
xcopy "Server\certs" "Release\Server\certs" /E /I /Y >nul
if exist "Release\Server\certs\ca.pfx" del /q "Release\Server\certs\ca.pfx"
if not exist "Release\Server\orig_gos2015.bin" copy /Y "Server\certs\orig_gos2015.bin" "Release\Server\" >nul

echo [4/4] Building launcher...
dotnet publish "Launcher\FUT_Reborn_Launcher.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "Release" || goto :fail
if exist Release\*.pdb del /q Release\*.pdb

echo.
echo ====================================================
echo  DONE. Zip the whole "Release" folder and share it.
echo  Anyone can extract it ANYWHERE and run FUT_Reborn_Launcher.exe
echo ====================================================
pause
exit /b 0
:fail
echo.
echo BUILD FAILED - send me the red error line above.
pause
exit /b 1
