param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'assets/showcase'
$taskFrames = Join-Path $taskRoot 'artifacts/showcase-frames'
New-Item -ItemType Directory -Force $taskFrames | Out-Null
$env:WINAPP_UI_WORKFLOW_ID = [Guid]::NewGuid().ToString()
$taskCli = (Get-AppxPackage -Name winapp).InstallLocation + '\winapp.exe'
function UI {
    $taskInfo = [Diagnostics.ProcessStartInfo]::new($taskCli)
    $taskInfo.UseShellExecute = $false; $taskInfo.CreateNoWindow = $true
    $taskInfo.RedirectStandardOutput = $true; $taskInfo.RedirectStandardError = $true
    foreach ($taskArgument in (@('ui') + $args + @('-a', "$AppPid"))) { $taskInfo.ArgumentList.Add("$taskArgument") }
    $taskProcess = [Diagnostics.Process]::Start($taskInfo)
    try {
        $taskText = $taskProcess.StandardOutput.ReadToEnd(); $taskError = $taskProcess.StandardError.ReadToEnd()
        $taskProcess.WaitForExit()
        if ($taskProcess.ExitCode) { throw "$($args -join ' '): $taskText $taskError" }
        $taskText
    } finally { $taskProcess.Dispose() }
}
try {
    UI invoke NavText | Out-Null
    UI wait-for TextGenerate -t 5000 | Out-Null
    UI set-value Field_文字内容 'ASCII' | Out-Null
    UI screenshot -o (Join-Path $taskFrames '01-input.png') | Out-Null
    UI invoke TextGenerate | Out-Null
    UI wait-for TextStatus --value '生成完成' -t 5000 | Out-Null
    UI screenshot -o (Join-Path $taskFrames '02-ascii.png') | Out-Null
    UI set-value Field_文字内容 'STUDIO' | Out-Null
    UI invoke TextGenerate | Out-Null
    UI wait-for TextStatus --value '生成完成' -t 5000 | Out-Null
    UI screenshot -o (Join-Path $taskFrames '03-studio.png') | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskFrames '02-ascii.png') -Destination (Join-Path $taskOutput 'app.png')
    Write-Output "Captured real application frames: $taskFrames"
} finally {
    & $taskCli ui yield | Out-Null
}
