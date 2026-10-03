param([Parameter(Mandatory)][string]$PublishDirectory, [switch]$KeepRunning)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$taskPreviousData = $env:ASCIISTUDIO_DATA_DIRECTORY
$taskPid = $null
try {
    foreach ($taskFile in @('AsciiStudio.exe', 'AsciiStudio.dll', 'AsciiStudio.pri', 'App.xbf', 'MainWindow.xbf')) {
        if (!(Test-Path -LiteralPath (Join-Path $taskDirectory $taskFile) -PathType Leaf)) { throw "Missing portable resource: $taskFile" }
    }
    $env:ASCIISTUDIO_DATA_DIRECTORY = Join-Path $taskRoot "artifacts/startup-tests/$([Guid]::NewGuid().ToString('N'))"
    # Evaluate the unpackaged project but launch only the extracted payload. No
    # build, registration or output-folder copying can repair it behind the test.
    $taskLaunch = & winapp run (Join-Path $taskRoot 'src/AsciiStudio/AsciiStudio.csproj') -c Release --arch x64 --no-build --detach --json -p "OutDir=$taskDirectory\"
    if ($LASTEXITCODE) { throw "Portable launch failed: $taskLaunch" }
    $taskLaunchInfo = ($taskLaunch -join "`n") | ConvertFrom-Json
    $taskPid = [int]$taskLaunchInfo.ProcessId
    if (!$taskPid) { throw "Launch returned no process ID: $taskLaunch" }
    & winapp ui wait-for NavHome -a $taskPid -t 10000
    if ($LASTEXITCODE) { throw 'Portable main window did not load.' }
    $taskProcess = Get-Process -Id $taskPid -ErrorAction Stop
    if ($taskProcess.Path -ne (Join-Path $taskDirectory 'AsciiStudio.exe')) { throw "Wrong executable tested: $($taskProcess.Path)" }
    if (Test-Path -LiteralPath (Join-Path $env:ASCIISTUDIO_DATA_DIRECTORY 'errors.log')) { throw 'Portable startup logged an unhandled error.' }
    Write-Output "PASS: extracted portable main window loaded (PID $taskPid): $($taskProcess.Path)"
} finally {
    $env:ASCIISTUDIO_DATA_DIRECTORY = $taskPreviousData
    if ($taskPid -and !$KeepRunning) { Stop-Process -Id $taskPid -ErrorAction SilentlyContinue }
}
