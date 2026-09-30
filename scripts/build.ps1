param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    # WinApp --no-launch applies only to MSIX projects. This existing unpackaged
    # project uses a build-only command with the skill analyzer in build props.
    $taskAnalyzer=Join-Path $env:USERPROFILE '.codex/skills/winui-dev-workflow/analyzer/Microsoft.WindowsAppSDK.Analyzers.dll'
    if(!(Test-Path -LiteralPath $taskAnalyzer)){throw 'Install microsoft/win-dev-skills before building.'}
    & dotnet build 'src/AsciiStudio/AsciiStudio.csproj' -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
} finally { Pop-Location }
