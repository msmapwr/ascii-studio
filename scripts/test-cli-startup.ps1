param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference='Stop'
$taskDirectory=(Resolve-Path -LiteralPath $PublishDirectory).Path
$taskExecutable=Join-Path $taskDirectory 'charloom-cli.exe'
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
$taskCurrentExecutable=$taskExecutable
$taskExecutable=Join-Path $taskDirectory 'asciistudio-cli.exe'
if (!(Test-Path -LiteralPath $taskExecutable)) { throw 'Legacy CLI entry missing.' }
$taskLegacyHelp=Invoke-StudioCli @('--help','--language','en-US')
if ($taskLegacyHelp -cne $taskHelp) { throw 'Legacy CLI entry differs from new entry.' }
$taskExecutable=$taskCurrentExecutable
Write-Output 'PASS legacy CLI entry loads the same Charloom implementation'
Write-Output "PASS standalone CLI help, UTF-8 stdin, FIGlet and honest capability reporting: $taskExecutable"
if ($taskCapabilities.result.complete -contains 'persistent-edit-history') {
    $taskProject=Join-Path $taskData 'edit-smoke.asciiproj'
    Invoke-StudioCli @('workspace','new','--project',$taskProject,'--text','a中b') | Out-Null
    Invoke-StudioCli @('edit','select','--column','1','--end-column','3') | Out-Null
    Invoke-StudioCli @('edit','replace','--selection','--text','X') | Out-Null
    if ((Invoke-StudioCli @('edit','show')) -cne 'aXb') { throw 'Cross-process edit was not persisted.' }
    Invoke-StudioCli @('history','undo') | Out-Null
    if ((Invoke-StudioCli @('edit','show')) -cne 'a中b') { throw 'Cross-process undo failed.' }
    Invoke-StudioCli @('history','redo') | Out-Null
    if ((Get-Content -LiteralPath $taskProject -Raw | ConvertFrom-Json).Document.Text -cne 'a中b') { throw 'Editing unexpectedly changed source project.' }
    Invoke-StudioCli @('workspace','close','--action','keep') | Out-Null
    Invoke-StudioCli @('workspace','restore') | Out-Null
    if ((Invoke-StudioCli @('edit','show')) -cne 'aXb') { throw 'Cross-process workspace recovery failed.' }
    Invoke-StudioCli @('project','save','--overwrite') | Out-Null
    if ((Get-Content -LiteralPath $taskProject -Raw | ConvertFrom-Json).Document.Text -cne 'aXb') { throw 'Explicit project save failed.' }
    Write-Output 'PASS cross-process Unicode editing, undo/redo, source protection, workspace recovery and explicit save'
}
if ($taskCapabilities.result.complete -contains 'candidate-result-management') {
    $taskProject=Join-Path $taskData 'candidate-smoke.asciiproj'
    Invoke-StudioCli @('text','--text','abc','--save-project',$taskProject) | Out-Null
    $taskOriginal=(Get-Content -LiteralPath $taskProject -Raw | ConvertFrom-Json).Document.Text
    Invoke-StudioCli @('edit','replace','--project',$taskProject,'--text','manual') | Out-Null
    Invoke-StudioCli @('candidate','create','--project',$taskProject) | Out-Null
    if ((Invoke-StudioCli @('edit','show','--project',$taskProject)) -cne 'manual') { throw 'Candidate replaced manual result.' }
    Invoke-StudioCli @('candidate','save','--project',$taskProject,'--output',(Join-Path $taskData 'candidate-copy.asciiproj')) | Out-Null
    Invoke-StudioCli @('candidate','accept','--project',$taskProject) | Out-Null
    Invoke-StudioCli @('history','undo','--project',$taskProject) | Out-Null
    if ((Invoke-StudioCli @('edit','show','--project',$taskProject)) -cne 'manual') { throw 'Candidate acceptance was not undoable.' }
    Invoke-StudioCli @('history','redo','--project',$taskProject) | Out-Null
    if ((Invoke-StudioCli @('edit','show','--project',$taskProject)) -cne $taskOriginal) { throw 'Candidate redo changed original generation.' }
    Write-Output 'PASS cross-process candidate generation, independent save, acceptance and undo/redo'
}
