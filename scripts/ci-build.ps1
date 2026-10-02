param([string]$Tag = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    [xml]$taskProject = Get-Content 'src/AsciiStudio/AsciiStudio.csproj' -Raw
    $taskVersion = [string]$taskProject.Project.PropertyGroup.Version
    if ($taskVersion -notmatch '^\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$') { throw "Unsupported release version: $taskVersion" }
    if ($Tag -and $Tag -cne "v$taskVersion") { throw "Tag $Tag does not match project version v$taskVersion" }
    [xml]$taskManifest = Get-Content 'packaging/Package.appxmanifest' -Raw
    $taskBaseVersion = $taskVersion.Split('-')[0]
    if ($taskManifest.Package.Identity.Version -ne "$taskBaseVersion.0") { throw 'Package manifest version does not match project version.' }
    $taskChangelog = Get-Content 'CHANGELOG.md' -Raw
    $taskPattern = '(?ms)^## \[' + [regex]::Escape($taskVersion) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## \[|\z)'
    $taskMatch = [regex]::Match($taskChangelog, $taskPattern)
    if (!$taskMatch.Success) { throw "Missing changelog section [$taskVersion]" }

    & dotnet restore 'src/AsciiStudio/AsciiStudio.csproj' --locked-mode
    if ($LASTEXITCODE) { throw 'Dependency restore failed.' }
    # CI has no desktop skill installation; local development retains its analyzer wrapper.
    & dotnet publish 'src/AsciiStudio/AsciiStudio.csproj' -c Release --no-restore --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/publish -v minimal
    if ($LASTEXITCODE) { throw 'Release compilation failed.' }
    & dotnet run --project 'tests/AsciiStudio.Core.Checks/AsciiStudio.Core.Checks.csproj' -c Release
    if ($LASTEXITCODE) { throw 'Core checks failed.' }
    & dotnet run --project 'tests/AsciiStudio.Text.Checks/AsciiStudio.Text.Checks.csproj' -c Release
    if ($LASTEXITCODE) { throw 'Windows text checks failed.' }

    foreach ($taskRequired in @('AsciiStudio.exe', 'AsciiStudio.dll', 'Microsoft.UI.Xaml.dll', 'Assets/AsciiStudio.ico')) {
        if (!(Test-Path -LiteralPath (Join-Path 'artifacts/publish' $taskRequired))) { throw "Missing publish payload: $taskRequired" }
    }
    $taskOutput = Join-Path $taskRoot 'artifacts/release'
    New-Item -ItemType Directory -Force $taskOutput | Out-Null
    $taskArchive = Join-Path $taskOutput "AsciiStudio-$taskVersion-win-x64.zip"
    # ZipFile supports cross-platform paths and preserves the complete self-contained payload.
    if (Test-Path -LiteralPath $taskArchive) { Remove-Item -LiteralPath $taskArchive }
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $taskRoot 'artifacts/publish'), $taskArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  $([IO.Path]::GetFileName($taskArchive))" | Set-Content (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding utf8NoBOM
    $taskNotes = "Windows x64 自包含便携版。解压完整目录后运行 AsciiStudio.exe。此 ZIP 未做代码签名。`n`n" + $taskMatch.Groups['notes'].Value.Trim()
    $taskNotes | Set-Content (Join-Path $taskOutput 'release-notes.md') -Encoding utf8NoBOM
    if ($env:GITHUB_OUTPUT) {
        "version=$taskVersion" | Add-Content $env:GITHUB_OUTPUT
        "prerelease=$($taskVersion.Contains('-').ToString().ToLowerInvariant())" | Add-Content $env:GITHUB_OUTPUT
    }
    Write-Output "Built $taskArchive"
} finally { Pop-Location }
