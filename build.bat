@echo off
setlocal

set "VERSION_NAME=dev"
where git >nul 2>&1
if errorlevel 1 (
    echo Git not found. Using dev as the version name.
    goto version_ready
)

set "EXACT_TAG="
for /f "delims=" %%T in ('git -C "%~dp0." describe --tags --exact-match HEAD 2^>nul') do set "EXACT_TAG=%%T"
if defined EXACT_TAG set "VERSION_NAME=%EXACT_TAG%"
if not defined EXACT_TAG for /f "delims=" %%H in ('git -C "%~dp0." rev-parse --short HEAD 2^>nul') do set "VERSION_NAME=%%H"

set "STATUS_FILE=%TEMP%\beatarenhax-status-%RANDOM%-%RANDOM%.txt"
git -C "%~dp0." status --porcelain --untracked-files=normal > "%STATUS_FILE%" 2>nul
for %%F in ("%STATUS_FILE%") do if %%~zF GTR 0 set "VERSION_NAME=%VERSION_NAME%+dirty"
del /q "%STATUS_FILE%" >nul 2>&1

:version_ready
echo Building BEAT ARENHAX %VERSION_NAME%
dotnet build "%~dp0BeatArenhax\BeatArenhax.csproj" -c Release "-p:BeatArenhaxVersion=%VERSION_NAME%"
if errorlevel 1 exit /b %errorlevel%

set "GAME_DIR=%~dp0.."
if not exist "%GAME_DIR%\winhttp.dll" (
    echo Modded game not found in the parent directory
    exit /b 1
)
for %%F in (coreclr.dll clrjit.dll System.Private.CoreLib.dll) do (
    if not exist "%GAME_DIR%\dotnet\%%F" (
        echo Required BepInEx runtime missing: dotnet\%%F
        echo Extract the complete Windows x64 IL2CPP BepInEx distribution into the game directory.
        exit /b 1
    )
    for %%F in (Assembly-CSharp.dll assembly-hash.txt) do (
        if not exist "%GAME_DIR%\BepInEx\interop\%%F" (
            echo Required generated interop file missing: BepInEx\interop\%%F
            echo Launch the game on Windows and wait for BepInEx chainloader startup to complete before packaging.
            exit /b 1
        )
    )
)

set "BUILD_DIR=%~dp0BeatArenhax\bin\Release\net6.0"
for %%F in (BeatArenhax.dll NAudio.Core.dll NAudio.Wasapi.dll NVorbis.dll NLayer.dll) do (
    if not exist "%BUILD_DIR%\%%F" (
        echo Required build output missing: %%F
        exit /b 1
    )
    if not exist "%BUILD_DIR%\Microsoft.Extensions.Configuration.Ini.dll" (
        echo Required build output missing: Microsoft.Extensions.Configuration.Ini.dll
        exit /b 1
    )
)

set "STAGE_DIR=%TEMP%\beatarenhax-package-%RANDOM%-%RANDOM%"
mkdir "%STAGE_DIR%\BepInEx\core" "%STAGE_DIR%\BepInEx\unity-libs" "%STAGE_DIR%\BepInEx\plugins\BeatArenhax" >nul
if errorlevel 1 goto package_failed

copy /y "%GAME_DIR%\winhttp.dll" "%STAGE_DIR%\" >nul
if errorlevel 1 goto package_failed
copy /y "%GAME_DIR%\doorstop_config.ini" "%STAGE_DIR%\" >nul
if errorlevel 1 goto package_failed
xcopy "%GAME_DIR%\dotnet" "%STAGE_DIR%\dotnet\" /e /i /h /y >nul
if errorlevel 1 goto package_failed
xcopy "%GAME_DIR%\BepInEx\interop" "%STAGE_DIR%\BepInEx\interop\" /e /i /h /y >nul
if errorlevel 1 goto package_failed
copy /y "%GAME_DIR%\BepInEx\core\*.dll" "%STAGE_DIR%\BepInEx\core\" >nul
if errorlevel 1 goto package_failed
for %%F in ("%GAME_DIR%\BepInEx\core\*.dll.config") do (
    if exist "%%~fF" (
        copy /y "%%~fF" "%STAGE_DIR%\BepInEx\core\" >nul
        if errorlevel 1 goto package_failed
    )
)
copy /y "%GAME_DIR%\BepInEx\unity-libs\*.dll" "%STAGE_DIR%\BepInEx\unity-libs\" >nul
if errorlevel 1 goto package_failed
for %%F in (BeatArenhax.dll NAudio.Core.dll NAudio.Wasapi.dll NVorbis.dll NLayer.dll) do (
    copy /y "%BUILD_DIR%\%%F" "%STAGE_DIR%\BepInEx\plugins\BeatArenhax\" >nul
    if errorlevel 1 goto package_failed
)
for %%F in ("%BUILD_DIR%\Microsoft.Extensions.*.dll") do (
    if exist "%%~fF" (
        copy /y "%%~fF" "%STAGE_DIR%\BepInEx\plugins\BeatArenhax\" >nul
        if errorlevel 1 goto package_failed
    )
)

set "ZIP_PATH=%~dp0beatarenhax-%VERSION_NAME%.zip"
if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"
pushd "%~dp0"
"%WINDIR%\System32\tar.exe" -a -c -f "beatarenhax-%VERSION_NAME%.zip" -C "%STAGE_DIR%" winhttp.dll doorstop_config.ini dotnet BepInEx
set "TAR_ERROR=%errorlevel%"
popd
if not "%TAR_ERROR%"=="0" goto package_failed
rmdir /s /q "%STAGE_DIR%"
echo Created %ZIP_PATH%
exit /b 0

:package_failed
if exist "%STAGE_DIR%" rmdir /s /q "%STAGE_DIR%"
echo Failed to assemble the release package.
exit /b 1
