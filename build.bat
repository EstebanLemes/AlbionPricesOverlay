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
echo [1/4] Compilando...
dotnet build --configuration Release -c Release
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

REM Publish
echo [2/4] Publicando...
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
echo [3/4] Creando ZIP...
mkdir Releases
powershell -command "Compress-Archive -Path 'AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\*' -DestinationPath 'Releases\AlbionPrices-%VERSION%.zip' -Force"
if errorlevel 1 (
    echo ERROR: Failed to create ZIP!
    pause
    exit /b 1
)

REM Update Inno Setup version
echo [4/4] Actualizando script de instalacion...
powershell -command "(Get-Content 'setup.iss') -replace 'MyAppVersion \"[^\"]*\"', 'MyAppVersion \"%VERSION%\"' | Set-Content 'setup.iss'"

echo.
echo ============================================
echo BUILD COMPLETO
echo ============================================
echo Version: %VERSION%
echo.
echo Archivos generados:
echo   - Releases\AlbionPrices-%VERSION%.zip
echo   - setup.iss (actualizado)
echo.
echo ============================================
echo.
echo PROXIMOS PASOS:
echo.
echo 1. Crear release en GitHub:
echo    - Tag: v%VERSION%
echo    - Titulo: Release v%VERSION%
echo    - Adjuntar: Releases\AlbionPrices-%VERSION%.zip
echo.
echo 2. Compilar installer (opcional):
echo    - Abre setup.iss con Inno Setup
echo    - Crea el installer desde Releases
echo.
echo 3. Para pruebas locales:
echo    - Instalar desde el ZIP o desde el installer
echo    - El sistema de updates detectara nuevas versiones
echo.

pause