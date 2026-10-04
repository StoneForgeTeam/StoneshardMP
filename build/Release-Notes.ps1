# A release's notes: its section of CHANGELOG.md ("## <version>", up to the next "## "), written to -Out.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Out
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$notes = @(); $inSection = $false; $found = $false
foreach ($line in Get-Content (Join-Path $root "CHANGELOG.md") -Encoding UTF8) {
    if ($line -match '^## ') {
        if ($inSection) { break }
        $inSection = $line -match "^## $([regex]::Escape($Version))\s*$"
        $found = $found -or $inSection
        continue
    }
    if ($inSection) { $notes += $line }
}
if (-not $found) { throw "CHANGELOG.md has no '## $Version' section." }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
[IO.File]::WriteAllText($Out, ($notes -join "`n").Trim() + "`n", (New-Object System.Text.UTF8Encoding $false))
