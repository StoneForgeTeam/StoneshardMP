# A release's version written into the mod: mod.json's "version", and CHANGELOG.md's "## Unreleased" section titled
# with it ("## 0.2.0"). A CHANGELOG that already has the version's section is left as it is; one with neither is an
# error - a release needs its notes. Only those two lines change (the files' formatting and line endings are kept).
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "'$Version' isn't a version (1.2.3, or 1.2.3-beta.1)." }
$root = Split-Path $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding $false

$modJson = Join-Path $root "mod.json"
$json = [IO.File]::ReadAllText($modJson)
$stamped = ([regex]'("version"\s*:\s*")[^"]*(")').Replace($json, "`${1}$Version`${2}", 1)
if ($stamped -eq $json -and $json -notmatch "`"version`"\s*:\s*`"$([regex]::Escape($Version))`"") { throw "mod.json has no ""version"" to set." }
[IO.File]::WriteAllText($modJson, $stamped, $utf8)

$changelog = Join-Path $root "CHANGELOG.md"
$text = [IO.File]::ReadAllText($changelog)
if ($text -match "(?m)^## $([regex]::Escape($Version))\s*$") {
    Write-Host "CHANGELOG.md already has a '## $Version' section."
}
elseif ($text -match '(?m)^## Unreleased\s*$') {
    $text = ([regex]'(?m)^## Unreleased(?=\s*$)').Replace($text, "## $Version", 1)
    [IO.File]::WriteAllText($changelog, $text, $utf8)
}
else {
    throw "CHANGELOG.md has no '## Unreleased' (or '## $Version') section: write the release's changes there first."
}
Write-Host "StoneshardMP $Version"
