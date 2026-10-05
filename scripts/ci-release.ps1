param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$') { throw 'Invalid release tag.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'artifacts/release'
$taskArchives = @(Get-ChildItem -LiteralPath $taskOutput -Filter "Charloom-$($Tag.Substring(1))-win-*.zip" | Where-Object { $_.Name -match '-win-(x64|arm64)\.zip$' } | Sort-Object Name)
if (!$taskArchives.Count) { throw 'Missing release archives.' }
$taskChecksums = Join-Path $taskOutput 'SHA256SUMS.txt'
$taskNotes = Join-Path $taskOutput 'release-notes.md'
foreach ($taskFile in @($taskChecksums, $taskNotes)) {
    if (!(Test-Path -LiteralPath $taskFile)) { throw "Missing release file: $taskFile" }
}
$taskExpected = (Get-Content $taskChecksums -Raw).Trim()
$taskActual = @($taskArchives | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
if ((@($taskExpected -split '\r?\n' | Sort-Object) -join "`n") -cne (($taskActual | Sort-Object) -join "`n")) { throw 'Release archive checksum mismatch.' }
$taskPrerelease = $Tag.Contains('-')
$taskExisting = & gh release view $Tag --json isDraft 2>$null
if ($LASTEXITCODE -eq 0) {
    if (!(($taskExisting | ConvertFrom-Json).isDraft)) { Write-Output "Release $Tag already published; leaving its assets unchanged."; return }
} else {
    $taskArguments = @('release', 'create', $Tag, '--verify-tag', '--draft', '--title', "Charloom $Tag", '--notes-file', $taskNotes)
    if ($taskPrerelease) { $taskArguments += '--prerelease' }
    & gh @taskArguments
    if ($LASTEXITCODE) { throw 'Could not create draft release.' }
}
& gh release upload $Tag @($taskArchives.FullName) $taskChecksums --clobber
if ($LASTEXITCODE) { throw 'Asset upload failed; release remains a draft.' }
$taskLatest = if ($taskPrerelease) { '--latest=false' } else { '--latest=true' }
& gh release edit $Tag --draft=false $taskLatest
if ($LASTEXITCODE) { throw 'Could not publish release.' }
