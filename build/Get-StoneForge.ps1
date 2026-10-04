# StoneForge.API.dll for building StoneshardMP without Stoneshard installed (CI). StoneForge.API is generated from the
# game's own data, so it can't be built on GitHub's runners - but every StoneForge release zip carries the one generated
# for it. Which: -Version, or the release mod.json asks for ("stoneforge"); "latest" (a mod in development) is
# StoneForge's newest release; "main" is StoneForge's main as it is now - its Main build workflow's rolling
# "main-latest" pre-release (StoneForge-main.zip), waited for (up to -WaitMinutes) until it's been made from main's
# head, so a StoneForge change pushed just before is in it.
# -Resolve: just the version that is ("latest" made the newest release's number), written instead of the folder.
# Writes the folder holding it (for -p:StoneForgeSdkDir=...); a release is downloaded once into .stoneforge\<version>,
# main each time. GITHUB_TOKEN, when set, is used for GitHub's API (its limits are far higher with it).
param(
    [string]$Version,
    [switch]$Resolve,
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot) ".stoneforge"),
    [int]$WaitMinutes = 15
)
$ErrorActionPreference = "Stop"
$repo = "StoneForgeTeam/StoneForge"
if (-not $Version) {
    $Version = (Get-Content (Join-Path (Split-Path $PSScriptRoot) "mod.json") -Raw | ConvertFrom-Json).stoneforge
    if (-not $Version) { throw "mod.json names no StoneForge version (""stoneforge"")." }
}

# GitHub's API, with the token when there is one.
function Get-GitHub([string]$path) {
    $headers = @{ "Accept" = "application/vnd.github+json"; "User-Agent" = "StoneshardMP-build" }
    if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }
    Invoke-RestMethod "https://api.github.com/repos/$repo/$path" -Headers $headers
}

# ("latest": the newest release's number.)
if ($Version -eq "latest") {
    $Version = (Get-GitHub "releases/latest").tag_name -replace '^v', ''
    Write-Host "StoneForge's newest release: $Version"
}
if ($Resolve) {
    return $Version
}

if ($Version -eq "main") {
    # main's head, and the commit main-latest is on: the same once its zip has been made from main as it is now.
    $deadline = (Get-Date).AddMinutes($WaitMinutes)
    while ($true) {
        $head = (Get-GitHub "commits/main").sha
        $built = $null
        try { $built = (Get-GitHub "commits/main-latest").sha } catch { }
        if ($built -eq $head) {
            Write-Host "StoneForge main-latest is main's head ($head)"
            break
        }
        if ((Get-Date) -ge $deadline) {
            throw "StoneForge main-latest ($built) still isn't main's head ($head) after $WaitMinutes minutes: see StoneForge's Main build workflow."
        }
        Write-Host "Waiting for StoneForge's Main build of $head (main-latest is on $built)..."
        Start-Sleep -Seconds 20
    }
}

$dir = Join-Path $Destination $Version
$api = Join-Path $dir "StoneForge.API.dll"
if ($Version -eq "main" -or -not (Test-Path $api)) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir "StoneForge-$Version.zip"
    $tag = if ($Version -eq "main") { "main-latest" } else { "v$Version" }
    $url = "https://github.com/$repo/releases/download/$tag/StoneForge-$Version.zip"
    Write-Host "Downloading StoneForge $Version ($url)"
    # (main's zip may still be uploading - the release, and its tag, can be up a moment before it: tried again until
    # the wait's up.)
    $until = (Get-Date).AddMinutes($(if ($Version -eq "main") { $WaitMinutes } else { 0 }))
    while ($true) {
        try {
            Invoke-WebRequest $url -OutFile $zip
            break
        }
        catch {
            if ((Get-Date) -ge $until) { throw "Couldn't download $url : $($_.Exception.Message)" }
            Write-Host "Not there yet ($($_.Exception.Message)) - trying again..."
            Start-Sleep -Seconds 20
        }
    }
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
