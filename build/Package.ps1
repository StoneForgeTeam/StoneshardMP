# The release zip: artifacts\StoneshardMP-<version>.zip, holding a StoneshardMP folder to put in Stoneshard\mods - the
# mod as the game loads it: mod.json, its .cs files (the game compiles them itself), lib\ (LiteNetLib), README.md,
# CHANGELOG.md. What's in it is what git tracks, less what's only for building it here (the .csproj, build\, .github\).
# -StoneForge: the StoneForge version the release names in its mod.json ("stoneforge"), in place of the checkout's
# "latest" - the release it was built against.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$StoneForge
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$manifest = Get-Content (Join-Path $root "mod.json") -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) { throw "mod.json's version is $($manifest.version), not $Version (build\Stamp-Version.ps1 sets it)." }

$artifacts = Join-Path $root "artifacts"
$stage = Join-Path $artifacts "StoneshardMP-$Version"
$folder = Join-Path $stage "StoneshardMP"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $folder | Out-Null

$files = git -C $root ls-files
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed" }
$count = 0
foreach ($file in $files) {
    if ($file -match '^(\.github|build)/' -or $file -match '^\.git' -or $file -like "*.csproj") { continue }
    $target = Join-Path $folder $file
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item (Join-Path $root $file) $target
    $count++
}
if ($StoneForge) {
    $manifest = Join-Path $folder "mod.json"
    $json = [IO.File]::ReadAllText($manifest)
    $json = ([regex]'("stoneforge"\s*:\s*")[^"]*(")').Replace($json, "`${1}$StoneForge`${2}", 1)
    [IO.File]::WriteAllText($manifest, $json, (New-Object System.Text.UTF8Encoding $false))
}
foreach ($required in "mod.json", "MultiplayerMod.cs", "lib/LiteNetLib.dll") {
    if (-not (Test-Path (Join-Path $folder $required))) { throw "The package has no $required." }
}
$zip = Join-Path $artifacts "StoneshardMP-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $folder -DestinationPath $zip
Write-Host "StoneshardMP $Version : $count files in $zip"
