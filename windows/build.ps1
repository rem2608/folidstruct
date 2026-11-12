<#
.SYNOPSIS
    Build FolidStruct for Windows: the File Explorer menu (C++) and the app,
    packaged as MSIX.

.EXAMPLE
    ./build.ps1                       # x64, package for testing (unsigned)
    ./build.ps1 -Platform ARM64
    ./build.ps1 -Store                # x64 + ARM64 bundle for the Microsoft Store

    Needs Visual Studio 2022 or later with the "Desktop development with C++"
    workload, and the .NET 10 SDK. Packages are written to windows/out/.
#>
param(
    [ValidateSet("x64", "ARM64")] [string] $Platform = "x64",
    [ValidateSet("Release", "Debug")] [string] $Configuration = "Release",
    [switch] $Store
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "out"

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "Visual Studio with the C++ tools was not found." }

function Invoke-Checked([string] $command, [string[]] $arguments) {
    Write-Host "> $command $($arguments -join ' ')"
    & $command @arguments
    if ($LASTEXITCODE -ne 0) { throw "$command failed with exit code $LASTEXITCODE" }
}

$platforms = if ($Store) { @("x64", "ARM64") } else { @($Platform) }

# 1. The menu handler, for every platform in the package.
foreach ($p in $platforms) {
    Invoke-Checked $msbuild @(
        (Join-Path $root "src\FolidStruct.ContextMenu\FolidStruct.ContextMenu.vcxproj"),
        "/m", "/nologo", "/v:minimal", "/p:Configuration=$Configuration", "/p:Platform=$p")
}

# 2. The app and its MSIX package.
$app = Join-Path $root "src\FolidStruct.App\FolidStruct.App.csproj"
$common = @("/p:Configuration=$Configuration", "/p:AppxPackageDir=$out\", "/p:GenerateAppxPackageOnBuild=true")
if ($Store) {
    foreach ($p in $platforms) {
        Invoke-Checked dotnet (@("build", $app, "-nologo", "/p:Platform=$p", "/p:UapAppxPackageBuildMode=StoreUpload",
                                 "/p:AppxBundle=Never") + $common)
    }
} else {
    Invoke-Checked dotnet (@("build", $app, "-nologo", "/p:Platform=$Platform",
                             "/p:UapAppxPackageBuildMode=SideloadOnly", "/p:AppxBundle=Never") + $common)
}

Write-Host "`nPackages:"
Get-ChildItem $out -Recurse -Include *.msix, *.msixupload, *.msixbundle | ForEach-Object { Write-Host "  $($_.FullName)" }
