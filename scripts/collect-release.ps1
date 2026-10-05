param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$') { throw 'Invalid version.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'artifacts/release'
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskHashes = foreach ($taskArchitecture in @('x64','arm64')) {
    $taskName = "Charloom-$Version-win-$taskArchitecture"
    $taskInput = Join-Path $taskRoot "artifacts/downloads/$taskName"
    $taskArchive = Join-Path $taskInput "$taskName.zip"
    $taskActual = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() + "  $taskName.zip"
    if ((Get-Content (Join-Path $taskInput 'SHA256SUMS.txt') -Raw).Trim() -cne $taskActual) { throw "Checksum mismatch: $taskName" }
    Copy-Item -LiteralPath $taskArchive -Destination $taskOutput -Force
    $taskActual
}
$taskHashes | Set-Content (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding utf8NoBOM
Copy-Item -LiteralPath (Join-Path $taskRoot "artifacts/downloads/Charloom-$Version-win-x64/release-notes.md") -Destination $taskOutput -Force
