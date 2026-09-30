param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
& (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $taskRoot 'BuildAndRun.ps1') (Join-Path $taskRoot 'src/AsciiStudio/AsciiStudio.csproj') -c $Configuration --arch x64 --detach
if($LASTEXITCODE -ne 0){throw "Run failed: $LASTEXITCODE"}
