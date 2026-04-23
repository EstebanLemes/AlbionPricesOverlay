# AlbionPrices Build Script (PowerShell)

param(
    [string]$GitHubOwner = "",
    [string]$GitHubRepo = "AlbionPricesOverlay",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

if (-not $GitHubOwner) {
    Write-Host "NOTA: Usa -GitHubOwner para crear release" -ForegroundColor Yellow
}

# Version
$csproj = Get-Content "AlbionPrices\AlbionPrices.csproj" -Raw
if ($csproj -match '<Version>([^<]+)</Version>') {
    $current = $matches[1]
} else { $current = "1.0.0" }

$parts = $current.Split('.')
if ($parts.Length -ge 3) {
    $next = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
} else { $next = "$current.1" }

$version = if ($Version) { $Version } else { $next }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "AlbionPrices v$version" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Actual: $current | Prox: $next" -ForegroundColor Gray

# Update csproj
$csproj = $csproj -replace '<Version>[^<]+</Version>', "<Version>$version</Version>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$version</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
Set-Content "AlbionPrices\AlbionPrices.csproj" -Value $csproj

# Update App.xaml.cs
$backup = ""
if ($GitHubOwner) {
    $app = Get-Content "AlbionPrices\App.xaml.cs" -Raw
    if ($app -match 'new UpdateService\("([^"]+)","([^"]+)"\)') {
        $backup = @{Owner=$matches[1]; Repo=$matches[2]}
        $app = $app -replace 'new UpdateService\("([^"]+)","([^"]+)"\)', "new UpdateService(`"$GitHubOwner`",`"$GitHubRepo`")"
        Set-Content "AlbionPrices\App.xaml.cs" -Value $app
    }
}

# Clean
if (Test-Path "Releases") { Remove-Item "Releases" -Recurse -Force }
if (Test-Path "Installer") { Remove-Item "Installer" -Recurse -Force }
$pub = "AlbionPrices\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }

# Build
Write-Host "[1/5] Build..." -ForegroundColor Yellow
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR Build"; exit 1 }

# Publish
Write-Host "[2/5] Publish..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -o $pub
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR Publish"; exit 1 }

# tessdata
if (Test-Path "tessdata") {
    $d = "$pub\tessdata"
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null }
    Copy-Item "tessdata\*" $d -Force
}

# ZIP
Write-Host "[3/5] ZIP..." -ForegroundColor Yellow
if (-not (Test-Path "Releases")) { New-Item -ItemType Directory -Path "Releases" | Out-Null }
Compress-Archive "$pub\*" -Destination "Releases\AlbionPrices-$version.zip" -Force

# setup.iss
Write-Host "[4/5] setup.iss..." -ForegroundColor Yellow
$iss = Get-Content "setup.iss" -Raw
$iss = $iss -replace 'MyAppVersion ".*"', "MyAppVersion `"$version`""
if ($GitHubOwner) {
    $iss = $iss -replace 'MyAppPublisher ".*"', "MyAppPublisher `"$GitHubOwner`""
    $iss = $iss -replace 'MyAppURL ".*"', "MyAppURL `"https://github.com/$GitHubOwner/$GitHubRepo`""
}
Set-Content "setup.iss" -Value $iss

# Installer
Write-Host "[5/5] Installer..." -ForegroundColor Yellow
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    & $iscc "setup.iss"
    if ($LASTEXITCODE -eq 0) { Write-Host "[OK] Installer" -ForegroundColor Green }
} else { Write-Host "WARN: Inno no encontrado" -ForegroundColor Yellow }

# GitHub Release
if ($GitHubOwner) {
    Write-Host "GitHub release..." -ForegroundColor Yellow
    try {
        $null = Get-Command gh -ErrorAction Stop
        $hasGh = $true
    } catch { $hasGh = $false }
    
    if ($hasGh) {
        $zipF = "Releases\AlbionPrices-$version.zip"
        $instF = "C:\Users\esteb\source\repos\Installer\AlbionPrices-Setup-$version.exe"
        if ((Test-Path $zipF) -or (Test-Path $instF)) {
            $args = @("release","create","v$version","--title","v$version","--notes","Build")
            if (Test-Path $zipF) { $args += $zipF }
            if (Test-Path $instF) { $args += $instF }
            & gh @args
            if ($LASTEXITCODE -eq 0) { Write-Host "[OK] GitHub" -ForegroundColor Green }
            else { Write-Host "WARN: gh error" -ForegroundColor Yellow }
        }
    } else {
        Write-Host "WARN: gh CLI no encontrado" -ForegroundColor Yellow
    }
}

# Restore App.xaml.cs
if ($backup) {
    $app = Get-Content "AlbionPrices\App.xaml.cs" -Raw
    $app = $app -replace 'new UpdateService\("([^"]+)","([^"]+)"\)', "new UpdateService(`"$($backup.Owner)`",`"$($backup.Repo)`")"
    Set-Content "AlbionPrices\App.xaml.cs" -Value $app
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "DONE: v$version" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "ZIP: Releases\AlbionPrices-$version.zip"
Write-Host "Installer: C:\Users\esteb\source\repos\Installer\AlbionPrices-Setup-$version.exe"
if ($GitHubOwner) {
    Write-Host "Release: https://github.com/$GitHubOwner/$GitHubRepo/releases/tag/v$version"
}