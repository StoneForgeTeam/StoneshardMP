# StoneForge.API.dll for building StoneshardMP without Stoneshard installed (CI). StoneForge.API is generated from the
# game's own data, so it can't be built on GitHub's runners - but every StoneForge release zip carries the one generated
# for it. Which: -Version, or the release mod.json asks for ("stoneforge"); "nightly" is StoneForge's main as it is now
# (its Nightly workflow's rolling pre-release, StoneForge-nightly.zip).
# Writes the folder holding it (for -p:StoneForgeSdkDir=...); downloaded once into .stoneforge\<version> (nightly: each
# time, it changes).
param(
    [string]$Version,
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot) ".stoneforge")
)
$ErrorActionPreference = "Stop"
if (-not $Version) {
    $Version = (Get-Content (Join-Path (Split-Path $PSScriptRoot) "mod.json") -Raw | ConvertFrom-Json).stoneforge
    if (-not $Version) { throw "mod.json names no StoneForge version (""stoneforge"")." }
}
$dir = Join-Path $Destination $Version
$api = Join-Path $dir "StoneForge.API.dll"
if ($Version -eq "nightly" -or -not (Test-Path $api)) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir "StoneForge-$Version.zip"
    $tag = if ($Version -eq "nightly") { "nightly" } else { "v$Version" }
    $url = "https://github.com/StoneForgeTeam/StoneForge/releases/download/$tag/StoneForge-$Version.zip"
    Write-Host "Downloading StoneForge $Version ($url)"
    Invoke-WebRequest $url -OutFile $zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($name in "StoneForge.API.dll", "StoneForge.API.xml") {
            $entry = $archive.Entries | Where-Object { $_.FullName -eq "files/dotnet/$name" } | Select-Object -First 1
            if ($entry) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $dir $name), $true) }
            elseif ($name -like "*.dll") { throw "StoneForge $Version's zip has no files/dotnet/$name." }
        }
    }
    finally { $archive.Dispose() }
    Remove-Item $zip
}
(Resolve-Path $dir).Path
