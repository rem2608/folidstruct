<#
.SYNOPSIS
    Install a test build of FolidStruct on this PC.

.DESCRIPTION
    Builds from GitHub Actions are signed with a throwaway test certificate
    (FolidStruct-test.cer, next to this script). The script trusts that
    certificate for app packages (Trusted People of this PC; this needs
    administrator rights) and installs the package like the Microsoft Store
    would.

    Without the certificate (e.g. a local unsigned build.ps1 build), the
    package is registered unpacked instead, which needs Developer Mode
    (Settings > System > For developers).

    The Microsoft Store version replaces either kind of test install.

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
$unpacked = Join-Path $env:LOCALAPPDATA "FolidStruct-dev"
$certificate = Join-Path $Path "FolidStruct-test.cer"
$trustedPeople = "Cert:\LocalMachine\TrustedPeople"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal] $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-FolidStruct {
    Get-AppxPackage -Name "*FolidStruct*" | ForEach-Object {
        Write-Host "Removing $($_.PackageFullName)"
        Remove-AppxPackage -Package $_.PackageFullName
    }
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
}

# Previous FolidStruct test certificates (the subject is the package publisher).
function Get-TestCertificates([string] $subject) {
    Get-ChildItem $trustedPeople | Where-Object { $_.Subject -eq $subject -and $_.Issuer -eq $subject }
}

if ($Uninstall) {
    Remove-FolidStruct
    if ((Test-Path $certificate) -and (Test-Administrator)) {
        $subject = ([Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)).Subject
        Get-TestCertificates $subject | Remove-Item
    }
    Write-Host "FolidStruct removed. Its settings are deleted with the package."
    return
}

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
$msix = Get-ChildItem $Path -Recurse -Filter *.msix |
    Where-Object { $_.FullName -notmatch "\\Dependencies\\" -and $_.Name -match "_$arch" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No FolidStruct .msix for $arch found in $Path." }
Write-Host "Package: $($msix.FullName)"

# The Windows App SDK runtime, shipped next to the package by the build.
$deps = @(Get-ChildItem $Path -Recurse -Filter *.msix | Where-Object { $_.FullName -match "\\Dependencies\\$arch\\" })
foreach ($dep in $deps) {
    Write-Host "Installing dependency $($dep.Name)"
    try { Add-AppxPackage -Path $dep.FullName -ErrorAction Stop }
    catch { Write-Host "  (already installed or newer: $($_.Exception.Message.Split("`n")[0]))" }
}
if (-not $deps -and -not (Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.8")) {
    Write-Warning ("Windows App SDK 1.8 runtime missing: install it from " +
                   "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-$arch.exe")
}

if (Test-Path $certificate) {
    # Signed test package: trust the certificate, then install normally.
    if (-not (Test-Administrator)) {
        Write-Host "Asking for administrator rights to trust the test certificate..."
        Start-Process powershell -Verb RunAs -Wait -ArgumentList @(
            "-ExecutionPolicy", "Bypass", "-NoProfile", "-File", "`"$PSCommandPath`"", "-Path", "`"$Path`"")
        return
    }
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)
    Get-TestCertificates $cert.Subject | Where-Object { $_.Thumbprint -ne $cert.Thumbprint } | Remove-Item
    Get-TestCertificates "CN=rem2608" | Remove-Item # test builds before the Store identity
    if (-not (Get-ChildItem $trustedPeople | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) {
        Import-Certificate -FilePath $certificate -CertStoreLocation $trustedPeople | Out-Null
        Write-Host "Test certificate trusted: $($cert.Subject), expires $($cert.NotAfter.ToShortDateString())"
    }
    Remove-FolidStruct
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
} else {
    # Unsigned package: register it unpacked (Developer Mode).
    $devMode = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" `
        -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
    if (-not $devMode -or $devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
        Write-Warning "Turn on Developer Mode first: Settings > System > For developers > Developer Mode."
        Start-Process "ms-settings:developers"
        return
    }
    Remove-FolidStruct
    New-Item -ItemType Directory $unpacked | Out-Null
    $zip = Join-Path $env:TEMP "FolidStruct-dev.zip"
    Copy-Item $msix.FullName $zip -Force
    Expand-Archive $zip -DestinationPath $unpacked -Force
    Remove-Item $zip
    foreach ($item in "AppxBlockMap.xml", "AppxSignature.p7x", "[Content_Types].xml", "AppxMetadata") {
        $file = Join-Path $unpacked $item
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Recurse -Force }
    }
    Add-AppxPackage -Register (Join-Path $unpacked "AppxManifest.xml") -ForceApplicationShutdown
}

Write-Host ""
Write-Host "FolidStruct installed. Open it from the Start menu, then right-click a folder"
Write-Host "in File Explorer. If the menu entry does not appear, restart File Explorer:"
Write-Host "  Stop-Process -Name explorer"
if (Test-Path $certificate) { Read-Host "Press Enter to close" | Out-Null }
