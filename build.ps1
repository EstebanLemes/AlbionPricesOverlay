# AlbionPrices Build Script - Windows + Linux
#   .\build.ps1 -GitHubOwner EstebanLemes
#   .\build.ps1 -GitHubOwner EstebanLemes -Version 1.0.9

param(
    [string]$GitHubOwner = "",
    [string]$GitHubRepo  = "AlbionPricesOverlay",
    [string]$Version     = ""
)

$ErrorActionPreference = "Stop"

# -- Versioning ----------------------------------------------------------------
$csproj = Get-Content "AlbionPrices\AlbionPrices.csproj" -Raw
if ($csproj -match '<Version>([^<]+)</Version>') { $current = $matches[1] } else { $current = "1.0.0" }
$parts  = $current.Split('.')
$next   = if ($parts.Length -ge 3) { "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)" } else { "$current.1" }
$version = if ($Version) { $Version } else { $next }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  AlbionPrices v$version  (Windows + Linux)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Current: $current  ->  New: $version" -ForegroundColor Gray
Write-Host ""

# -- Bump version in csproj ----------------------------------------------------
$csproj = $csproj -replace '<Version>[^<]+</Version>',         "<Version>$version</Version>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$version</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
Set-Content "AlbionPrices\AlbionPrices.csproj" -Value $csproj

# -- Paths ---------------------------------------------------------------------
$pubWin   = "AlbionPrices\bin\Release\net10.0\win-x64\publish"
$pubLinux = "AlbionPrices\bin\Release\net10.0\linux-x64\publish"
$zipWin   = "Releases\AlbionPrices-$version-windows.zip"
$zipLinux = "Releases\AlbionPrices-$version-linux.zip"

# Clean
@($pubWin, $pubLinux) | ForEach-Object { if (Test-Path $_) { Remove-Item $_ -Recurse -Force } }
if (-not (Test-Path "Releases")) { New-Item -ItemType Directory -Path "Releases" | Out-Null }

# -- [1/5] Build ---------------------------------------------------------------
Write-Host "[1/5] Build..." -ForegroundColor Yellow
dotnet build AlbionPrices\AlbionPrices.csproj -c Release
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: build failed" -ForegroundColor Red; exit 1 }

# -- [2/5] Publish Windows -----------------------------------------------------
Write-Host "[2/5] Publish Windows (win-x64)..." -ForegroundColor Yellow
dotnet publish AlbionPrices\AlbionPrices.csproj `
    -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=false `
    -o $pubWin
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: publish windows failed" -ForegroundColor Red; exit 1 }

# Copy tessdata into Windows publish
if (Test-Path "tessdata") {
    $td = "$pubWin\tessdata"
    if (-not (Test-Path $td)) { New-Item -ItemType Directory -Path $td | Out-Null }
    Copy-Item "tessdata\*" $td -Force
}

Compress-Archive "$pubWin\*" -Destination $zipWin -Force
Write-Host "  -> $zipWin" -ForegroundColor Green

# -- [3/5] Publish Linux -------------------------------------------------------
Write-Host "[3/5] Publish Linux (linux-x64)..." -ForegroundColor Yellow
dotnet publish AlbionPrices\AlbionPrices.csproj `
    -c Release -r linux-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $pubLinux
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: publish linux failed" -ForegroundColor Red; exit 1 }

# Copy tessdata into Linux publish
if (Test-Path "tessdata") {
    $td = "$pubLinux\tessdata"
    if (-not (Test-Path $td)) { New-Item -ItemType Directory -Path $td | Out-Null }
    Copy-Item "tessdata\*" $td -Force
}

# Generate install.sh
$installSh = @'
#!/bin/bash
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="$HOME/.local/share/AlbionPrices"
APPS_DIR="$HOME/.local/share/applications"
DESKTOP_DIR="$HOME/Desktop"

echo "=== AlbionPrices Installer ==="

# Install dependencies (Debian/Ubuntu)
if command -v apt-get &>/dev/null; then
    missing=()
    for pkg in libfontconfig1 libice6 libsm6; do
        dpkg -s "$pkg" &>/dev/null 2>&1 || missing+=("$pkg")
    done
    if [ ${#missing[@]} -gt 0 ]; then
        echo "Installing dependencies: ${missing[*]}"
        sudo apt-get install -y "${missing[@]}"
    fi
fi

# Copy files
echo "Installing to $INSTALL_DIR ..."
mkdir -p "$INSTALL_DIR"
cp -r "$DIR"/. "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/AlbionPrices"

# Create .desktop entry
mkdir -p "$APPS_DIR"
cat > "$APPS_DIR/albionprices.desktop" <<EOF
[Desktop Entry]
Name=AlbionPrices
Comment=Albion Online price overlay
Exec=$INSTALL_DIR/AlbionPrices
Icon=$INSTALL_DIR/app.ico
Type=Application
Categories=Game;Utility;
StartupNotify=false
EOF
chmod +x "$APPS_DIR/albionprices.desktop"

# Desktop shortcut
if [ -d "$DESKTOP_DIR" ]; then
    cp "$APPS_DIR/albionprices.desktop" "$DESKTOP_DIR/AlbionPrices.desktop"
    chmod +x "$DESKTOP_DIR/AlbionPrices.desktop"
    echo "Desktop shortcut created."
fi

# Refresh app menu
update-desktop-database "$APPS_DIR" 2>/dev/null || true

echo ""
echo "Done! AlbionPrices installed."
echo "Launch from your app menu or desktop shortcut."
echo ""
read -p "Launch now? [Y/n] " ans
if [[ "$ans" != "n" && "$ans" != "N" ]]; then
    "$INSTALL_DIR/AlbionPrices" &
fi
'@
$installSh | Set-Content "$pubLinux\install.sh" -Encoding utf8

Compress-Archive "$pubLinux\*" -Destination $zipLinux -Force
Write-Host "  -> $zipLinux" -ForegroundColor Green

# -- [4/5] Windows Installer (Inno Setup, optional) ----------------------------
Write-Host "[4/5] Installer Windows..." -ForegroundColor Yellow
$iss  = Get-Content "setup.iss" -Raw
$iss  = $iss -replace 'MyAppVersion "[^"]*"',   "MyAppVersion `"$version`""
$iss  = $iss -replace 'net10\.0[^\\]*\\win-x64', "net10.0\win-x64"
Set-Content "setup.iss" -Value $iss

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$installerPath = $null
if (Test-Path $iscc) {
    & $iscc "setup.iss"
    if ($LASTEXITCODE -eq 0) {
        $installerPath = "..\Installer\AlbionPrices-Setup-$version.exe"
        Write-Host "  -> $installerPath" -ForegroundColor Green
    } else {
        Write-Host "  WARN: Inno Setup failed, uploading ZIP only" -ForegroundColor Yellow
    }
} else {
    Write-Host "  WARN: Inno Setup not found, uploading ZIP only" -ForegroundColor Yellow
}

# -- [5/5] GitHub Release ------------------------------------------------------
if ($GitHubOwner) {
    Write-Host "[5/5] GitHub Release v$version..." -ForegroundColor Yellow
    try { $null = Get-Command gh -ErrorAction Stop; $hasGh = $true } catch { $hasGh = $false }

    if ($hasGh) {
        $notes = "## AlbionPrices v$version`n`n" +
                 "### Windows`n" +
                 "1. Download AlbionPrices-$version-windows.zip`n" +
                 "2. Extract and run AlbionPrices.exe`n`n" +
                 "### Linux`n" +
                 "1. Download AlbionPrices-$version-linux.zip`n" +
                 "2. Extract the zip`n" +
                 "3. Open a terminal in the extracted folder and run: chmod +x install.sh && ./install.sh`n" +
                 "4. The installer creates a desktop shortcut and app menu entry automatically`n`n" +
                 "Linux requires: libfontconfig1, libice6, libsm6 (installed automatically on Debian/Ubuntu)"

        $releaseArgs = @(
            "release", "create", "v$version",
            "--repo", "$GitHubOwner/$GitHubRepo",
            "--title", "v$version",
            "--notes", $notes
        )

        # Attach files
        if (Test-Path $zipWin)   { $releaseArgs += $zipWin }
        if (Test-Path $zipLinux) { $releaseArgs += $zipLinux }
        if ($installerPath -and (Test-Path $installerPath)) { $releaseArgs += $installerPath }

        & gh @releaseArgs
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  -> https://github.com/$GitHubOwner/$GitHubRepo/releases/tag/v$version" -ForegroundColor Green
        } else {
            Write-Host "  WARN: gh release failed - upload ZIPs manually" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  WARN: gh CLI not found. Install from https://cli.github.com" -ForegroundColor Yellow
    }
} else {
    Write-Host "[5/5] GitHub Release skipped (use -GitHubOwner EstebanLemes)" -ForegroundColor Gray
}

# -- Summary -------------------------------------------------------------------
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  DONE: v$version" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Windows : $zipWin"
Write-Host "  Linux   : $zipLinux"
if ($installerPath) { Write-Host "  Setup   : $installerPath" }
if ($GitHubOwner)   { Write-Host "  Release : https://github.com/$GitHubOwner/$GitHubRepo/releases/tag/v$version" }
Write-Host ""
