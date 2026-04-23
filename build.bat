@echo off
setlocal enabledelayedexpansion

echo ============================================
echo AlbionPrices - Build and Release Script
echo ============================================
echo.

REM Get version from csproj
for /f "tokens=2 delims=<>" %%a in ('findstr /i "Version" AlbionPrices\AlbionPrices.csproj ^| findstr /i "Version>"') do (
    set VERSION=%%a
)
if not defined VERSION set VERSION=1.0.0

echo Version actual: %VERSION%
echo.

REM Clean previous builds
if exist "Releases" rmdir /s /q "Releases"
if exist "Installer" rmdir /s /q "Installer"
if exist "AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish" rmdir /s /q "AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish"

REM Build
echo [1/5] Compilando...
dotnet build -c Release
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

REM Publish
echo [2/5] Publicando...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish
if errorlevel 1 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

REM Copy tessdata
if exist "tessdata" (
    if not exist "AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\tessdata" mkdir "AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\tessdata"
    xcopy /y /e /q "tessdata\*" "AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\tessdata\" >nul
    echo [OK] tessdata copiado
)

REM Create ZIP
echo [3/5] Creando ZIP...
mkdir Releases
powershell -command "Compress-Archive -Path 'AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\*' -DestinationPath 'Releases\AlbionPrices-%VERSION%.zip' -Force"
if errorlevel 1 (
    echo ERROR: Failed to create ZIP!
    pause
    exit /b 1
)

REM Update Inno Setup version
echo [4/5] Actualizando script de instalacion...
(
echo $version = "%VERSION%"
echo $content = Get-Content setup.iss -Raw
echo $newContent = $content -replace 'MyAppVersion ".*"', "MyAppVersion ""$version"""
echo Set-Content setup.iss -Value $newContent
) > update_version.ps1
powershell -ExecutionPolicy Bypass -File update_version.ps1
del update_version.ps1

REM Compile installer with Inno Setup
echo [5/5] Compilando installer...
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    echo Compilando installer con Inno Setup...
    cmd /c ""C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss"
    if errorlevel 1 (
        echo ERROR: Installer compilation failed!
        pause
        exit /b 1
    )
    echo [OK] Installer creado
) else (
    echo ADVERTENCIA: Inno Setup no encontrado
    pause
    exit /b 1
)

echo.
echo ============================================
echo BUILD COMPLETO
echo ============================================
echo Version: %VERSION%
echo.
echo Archivos generados:
echo   - Releases\AlbionPrices-%VERSION%.zip
echo   - Installer\AlbionPrices-Setup-%VERSION%.exe
echo   - setup.iss (actualizado)
echo.
echo ============================================
echo.
echo PROXIMOS PASOS:
echo.
echo 1. Verificar Release:
echo    - Releases\AlbionPrices-%VERSION%.zip
echo    - Installer\AlbionPrices-Setup-%VERSION%.exe
echo.
echo 2. Probar installer o ZIP
echo.

pause