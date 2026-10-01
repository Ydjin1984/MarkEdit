@echo off
rem Builds the editor core and publishes MarkEdit for Windows.
rem
rem Usage:
rem   build.cmd              self-contained single file (needs no .NET runtime)
rem   build.cmd framework    framework dependent (needs the .NET Desktop Runtime)

setlocal enabledelayedexpansion
cd /d "%~dp0"

set MODE=%1
if "%MODE%"=="" set MODE=selfcontained

echo.
echo === 1/3  Building the editor core (CoreEditor) ===
pushd "..\CoreEditor"
if not exist "node_modules" (
  echo Installing npm dependencies...
  call node ".yarn\releases\yarn-4.18.0.cjs" install || goto :failed
)

call node ".yarn\releases\yarn-4.18.0.cjs" build || goto :failed
popd

echo.
echo === 2/3  Publishing MarkEdit for Windows (%MODE%) ===
set OUTDIR=..\..\dist\windows
if "%MODE%"=="framework" (
  set PUBFLAGS=--self-contained false
) else (
  set PUBFLAGS=--self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
)

dotnet publish "MarkEditWin\MarkEditWin.csproj" -c Release -r win-x64 %PUBFLAGS% -o "%OUTDIR%" || goto :failed

echo.
echo === 3/3  Done ===
echo Output: %CD%\%OUTDIR%
dir /b "%OUTDIR%\MarkEdit.exe"
echo.
echo Optional smoke test (opens a hidden window):
echo   "%OUTDIR%\MarkEdit.exe" --self-test path\to\document.md report.txt
exit /b 0

:failed
echo.
echo BUILD FAILED
exit /b 1
