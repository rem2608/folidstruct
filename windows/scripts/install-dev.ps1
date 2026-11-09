<#
.SYNOPSIS
    Install a test build of FolidStruct (unsigned MSIX) on this PC.

.DESCRIPTION
    Builds from GitHub Actions and build.ps1 are not signed: Windows only
    installs them as a "development" package, which needs Developer Mode
    (Settings > System > For developers > Developer Mode).

    The script unpacks the .msix into %LOCALAPPDATA%\FolidStruct-dev and
    registers it; the Microsoft Store version replaces it later.

.EXAMPLE
    # From the folder of the downloaded "FolidStruct-windows-x64" artifact:
    powershell -ExecutionPolicy Bypass -File install-dev.ps1

.EXAMPLE
    install-dev.ps1 -Uninstall
#>
param(
    # Folder with the .msix (default: the folder of this script and below).
    [string] $Path = $PSScriptRoot,
    [switch] $Uninstall
)

$ErrorActionPreference = "Stop"
$target = Join-Path $env:LOCALAPPDATA "FolidStruct-dev"

function Remove-FolidStruct {
    Get-AppxPackage -Name "*FolidStruct*" | ForEach-Object {
        Write-Host "Removing $($_.PackageFullName)"
        Remove-AppxPackage -Package $_.PackageFullName
    }
}

if ($Uninstall) {
    Remove-FolidStruct
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Write-Host "FolidStruct removed. Its settings are deleted with the package."
    return
}

# Developer Mode is needed to register an unsigned package.
$devMode = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" `
    -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if (-not $devMode -or $devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    Write-Warning "Turn on Developer Mode first: Settings > System > For developers > Developer Mode."
    Start-Process "ms-settings:developers"
    return
}

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
$msix = Get-ChildItem $Path -Recurse -Filter *.msix |
    Where-Object { $_.FullName -notmatch "\\Dependencies\\" -and $_.Name -match "_$arch" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No FolidStruct .msix for $arch found in $Path." }
Write-Host "Package: $($msix.FullName)"

# The Windows App SDK runtime: shipped next to the package by the build.
$deps = Get-ChildItem $Path -Recurse -Filter *.msix |
    Where-Object { $_.FullName -match "\\Dependencies\\$arch\\" }
foreach ($dep in $deps) {
    Write-Host "Installing dependency $($dep.Name)"
    try { Add-AppxPackage -Path $dep.FullName -ErrorAction Stop }
    catch { Write-Host "  (already installed or newer: $($_.Exception.Message.Split("`n")[0]))" }
}
if (-not $deps -and -not (Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.8")) {
    Write-Warning ("Windows App SDK 1.8 runtime missing: install it from " +
                   "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-$arch.exe")
}

# Unpack and register.
Remove-FolidStruct
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory $target | Out-Null
$zip = Join-Path $env:TEMP "FolidStruct-dev.zip"
Copy-Item $msix.FullName $zip -Force
Expand-Archive $zip -DestinationPath $target -Force
Remove-Item $zip
foreach ($item in "AppxBlockMap.xml", "AppxSignature.p7x", "[Content_Types].xml", "AppxMetadata") {
    $file = Join-Path $target $item
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Recurse -Force }
}
Add-AppxPackage -Register (Join-Path $target "AppxManifest.xml") -ForceApplicationShutdown

Write-Host ""
Write-Host "FolidStruct installed. Open it from the Start menu, then right-click a folder"
Write-Host "in File Explorer. If the menu entry does not appear, restart File Explorer:"
Write-Host "  Stop-Process -Name explorer"
