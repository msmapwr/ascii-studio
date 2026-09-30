param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/build.ps1" -Configuration $Configuration
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = Join-Path $taskRoot "src/AsciiStudio/bin/$Configuration/net10.0-windows10.0.26100.0/win-x64/AsciiStudio.exe"
Start-Process -FilePath $taskExe
