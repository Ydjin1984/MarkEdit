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
echo === 1/4  Building the editor core (CoreEditor) ===
pushd "..\CoreEditor"
if not exist "node_modules" (
  echo Installing npm dependencies...
  call node ".yarn\releases\yarn-4.18.0.cjs" install || goto :failed
)

call node ".yarn\releases\yarn-4.18.0.cjs" build || goto :failed
popd

echo.
echo === 2/4  Publishing MarkEdit for Windows (%MODE%) ===
rem The publish folder is set in MarkEditWin.csproj for Release builds, so no -o here: -o would
rem also redirect the intermediate build output and mix the framework assemblies into it.
set "OUTDIR=%~dp0..\dist\windows"
set "RELDIR=%~dp0..\dist\release"
set "ZIPFILE=%~dp0..\dist\MarkEdit-win-x64.zip"

if "%MODE%"=="framework" (
  set PUBFLAGS=--self-contained false
) else (
  set PUBFLAGS=--self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
)

dotnet publish "MarkEditWin\MarkEditWin.csproj" -c Release -r win-x64 %PUBFLAGS% || goto :failed

echo.
echo === 3/4  Packaging ===
rem Only the single file bundle is shipped, the loose assemblies in the publish folder are the
rem intermediate build output and are not needed next to it.
if exist "%RELDIR%" rmdir /s /q "%RELDIR%"
mkdir "%RELDIR%" || goto :failed
copy /y "%OUTDIR%\MarkEdit.exe" "%RELDIR%\" >nul || goto :failed
copy /y "README.md" "%RELDIR%\README.md" >nul
copy /y "..\LICENSE" "%RELDIR%\LICENSE" >nul
copy /y "test\sample.md" "%RELDIR%\sample.md" >nul

if exist "%ZIPFILE%" del "%ZIPFILE%"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Compress-Archive -Path '%RELDIR%\*' -DestinationPath '%ZIPFILE%' -Force" || goto :failed

echo.
echo === 4/4  Done ===
echo Single file bundle: %OUTDIR%\MarkEdit.exe
echo Release folder:     %RELDIR%
echo Release archive:    %ZIPFILE%
echo.
echo Optional smoke test (runs a hidden window):
echo   "%OUTDIR%\MarkEdit.exe" --self-test test\sample.md report.txt
exit /b 0

:failed
echo.
echo BUILD FAILED
exit /b 1
