param([string]$Tag = '', [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
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

    & dotnet restore 'src/AsciiStudio/AsciiStudio.csproj' --locked-mode "-p:StudioArchitecture=$Architecture"
    if ($LASTEXITCODE) { throw 'Dependency restore failed.' }
    [xml]$taskCliProject = Get-Content 'src/AsciiStudio.Cli/AsciiStudio.Cli.csproj' -Raw
    if ([string]$taskCliProject.Project.PropertyGroup.Version -ne $taskVersion) { throw 'CLI/GUI version mismatch.' }
    & dotnet restore 'src/AsciiStudio.Cli/AsciiStudio.Cli.csproj' --locked-mode "-p:StudioArchitecture=$Architecture"
    if ($LASTEXITCODE) { throw 'CLI dependency restore failed.' }
    # A fresh directory prevents an old PRI/XBF from hiding a broken publish.
    $taskPublish = [IO.Path]::GetFullPath((Join-Path $taskRoot "artifacts/publish-$Architecture"))
    $taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (!$taskPublish.StartsWith($taskArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish directory escapes artifacts.' }
    if (Test-Path -LiteralPath $taskPublish) { Remove-Item -LiteralPath $taskPublish -Recurse -Force }
    # CI has no desktop skill installation; local development retains its analyzer wrapper.
    & dotnet publish 'src/AsciiStudio/AsciiStudio.csproj' -c Release --no-restore "-p:StudioArchitecture=$Architecture" --self-contained true -p:WindowsAppSDKSelfContained=true -o $taskPublish -v minimal
    if ($LASTEXITCODE) { throw 'Release compilation failed.' }
    & dotnet publish 'src/AsciiStudio.Cli/AsciiStudio.Cli.csproj' -c Release --no-restore "-p:StudioArchitecture=$Architecture" --self-contained true -o $taskPublish -v minimal
    if ($LASTEXITCODE) { throw 'CLI release compilation failed.' }
    & dotnet test 'tests/AsciiStudio.Core.Tests/AsciiStudio.Core.Tests.csproj' -c Release --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE) { throw 'Core unit tests failed.' }
    & dotnet test 'tests/AsciiStudio.Creation.Tests/AsciiStudio.Creation.Tests.csproj' -c Release --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE) { throw 'Creation controller unit tests failed.' }
    & dotnet test 'tests/AsciiStudio.Cli.Tests/AsciiStudio.Cli.Tests.csproj' -c Release --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE) { throw 'CLI unit tests failed.' }

    foreach ($taskRequired in @('AsciiStudio.exe', 'AsciiStudio.dll', 'AsciiStudio.pri', 'App.xbf', 'MainWindow.xbf', 'Microsoft.UI.Xaml.dll', 'Assets/AsciiStudio.ico', 'asciistudio-cli.exe', 'asciistudio-cli.dll', 'asciistudio-cli.runtimeconfig.json', 'AsciiStudio.Application.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $taskPublish $taskRequired))) { throw "Missing publish payload: $taskRequired" }
    }
    # Hosted runners are x64; ARM64 is cross-built and requires separate device acceptance.
    if ($Architecture -eq 'x64') {
        & (Join-Path $PSScriptRoot 'test-cli-startup.ps1') -PublishDirectory $taskPublish
        & (Join-Path $PSScriptRoot 'test-cli-tutorial.ps1') -PublishDirectory $taskPublish
    }
    $taskOutput = Join-Path $taskRoot 'artifacts/release'
    New-Item -ItemType Directory -Force $taskOutput | Out-Null
    $taskArchive = Join-Path $taskOutput "AsciiStudio-$taskVersion-win-$Architecture.zip"
    # ZipFile supports cross-platform paths and preserves the complete self-contained payload.
    if (Test-Path -LiteralPath $taskArchive) { Remove-Item -LiteralPath $taskArchive }
    [IO.Compression.ZipFile]::CreateFromDirectory($taskPublish, $taskArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskChecksumFile = Join-Path $taskOutput 'SHA256SUMS.txt'
    $taskOtherHashes = if (Test-Path $taskChecksumFile) { @(Get-Content $taskChecksumFile | Where-Object { $_ -match '^[a-f0-9]{64}  AsciiStudio-' -and $_ -like "*AsciiStudio-$taskVersion-win-*" -and $_ -notlike "*win-$Architecture.zip" }) } else { @() }
    @($taskOtherHashes; "$taskHash  $([IO.Path]::GetFileName($taskArchive))") | Set-Content $taskChecksumFile -Encoding utf8NoBOM
    $taskNotes = "Windows x64 / ARM64 自包含便携版，包含桌面端 AsciiStudio.exe 与独立控制台 asciistudio-cli.exe。选择对应架构并完整解压；CLI 从 --help 开始。ZIP 未做代码签名；ARM64 实机验收另行记录。`n`n" + $taskMatch.Groups['notes'].Value.Trim()
    $taskNotes | Set-Content (Join-Path $taskOutput 'release-notes.md') -Encoding utf8NoBOM
    if ($env:GITHUB_OUTPUT) {
        "version=$taskVersion" | Add-Content $env:GITHUB_OUTPUT
        "prerelease=$($taskVersion.Contains('-').ToString().ToLowerInvariant())" | Add-Content $env:GITHUB_OUTPUT
    }
    Write-Output "Built $taskArchive"
} finally { Pop-Location }
