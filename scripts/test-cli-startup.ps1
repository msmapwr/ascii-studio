param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference='Stop'
$taskDirectory=(Resolve-Path -LiteralPath $PublishDirectory).Path
$taskExecutable=Join-Path $taskDirectory 'asciistudio-cli.exe'
$taskData=Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/cli-startup-tests/$([Guid]::NewGuid().ToString('N'))"
function Invoke-StudioCli([string[]]$Arguments,[string]$InputText='') {
    $taskStart=[Diagnostics.ProcessStartInfo]::new($taskExecutable)
    $taskStart.UseShellExecute=$false; $taskStart.CreateNoWindow=$true
    $taskStart.RedirectStandardInput=$true; $taskStart.RedirectStandardOutput=$true; $taskStart.RedirectStandardError=$true
    $taskStart.StandardInputEncoding=[Text.UTF8Encoding]::new($false)
    $taskStart.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
    foreach($taskArgument in (@('--data-directory',$taskData,'--font-directory',(Join-Path $taskData 'fonts'))+$Arguments)) { $taskStart.ArgumentList.Add($taskArgument) }
    $taskProcess=[Diagnostics.Process]::Start($taskStart)
    try {
        $taskOut=$taskProcess.StandardOutput.ReadToEndAsync(); $taskError=$taskProcess.StandardError.ReadToEndAsync()
        $taskProcess.StandardInput.Write($InputText); $taskProcess.StandardInput.Close()
        if (!$taskProcess.WaitForExit(30000)) { $taskProcess.Kill(); throw 'CLI startup exceeded 30 seconds.' }
        if ($taskProcess.ExitCode) { throw "CLI failed: $($taskError.GetAwaiter().GetResult())" }
        $taskOut.GetAwaiter().GetResult()
    } finally { $taskProcess.Dispose() }
}
$taskHelp=Invoke-StudioCli @('--help','--language','en-US')
if ($taskHelp -notlike '*Exit codes:*' -or $taskHelp -notlike '*project regenerate*') { throw 'Incomplete root help.' }
$taskImageHelp=Invoke-StudioCli @('image','--help','--language','zh-CN')
if ($taskImageHelp -notlike '*geometry.QuarterTurns*' -or $taskImageHelp -notlike '*StructureThreshold*') { throw 'Image option help missing.' }
$taskEncoded=Invoke-StudioCli @('crypto','apply','--algorithm','UTF-8','--stdin') '中🙂'
if ($taskEncoded -cne 'E4B8ADF09F9982') { throw "UTF-8 stdin/stdout mismatch: $taskEncoded" }
$taskText=Invoke-StudioCli @('text','--text','abc')
if ($taskText -notlike '*_*') { throw 'FIGlet CLI produced no expected output.' }
$taskCapabilities=Invoke-StudioCli @('capabilities','--json') | ConvertFrom-Json
if ($taskCapabilities.result.desktopParityComplete -ne $false -or !$taskCapabilities.result.pending.Count) { throw 'Prerelease parity status is inaccurate.' }
Write-Output "PASS standalone CLI help, UTF-8 stdin, FIGlet and honest capability reporting: $taskExecutable"
