param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$DataDirectory)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskHwnd=(& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json | Where-Object ownerHwnd -eq 0 | Select-Object -First 1).hwnd
if (!$taskHwnd) { throw 'Test window not found.' }
$taskResults=[Collections.Generic.List[object]]::new()
function UI {
    $taskReply=& winapp ui @args -w $taskHwnd 2>&1
    if ($LASTEXITCODE) {
        # Search uses a nonzero status for an empty match set. This is expected
        # while awaiting responsive navigation; assertions still use wait-for.
        if ($args[0] -eq 'search') {
            try { $taskSearch=($taskReply -join "`n") | ConvertFrom-Json; if ($taskSearch.matchCount -eq 0 -and $null -ne $taskSearch.matches) { return $taskReply } }
            catch { }
        }
        throw ($taskReply -join "`n")
    }
    $taskReply
}
function Check([string]$Name,[scriptblock]$Action) {
    try { & $Action; $taskResults.Add([pscustomobject]@{name=$Name;status='PASS'}) }
    catch { $taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message}) }
}
Check 'Shared FIGlet renderer works in desktop' {
    UI invoke NavText | Out-Null
    UI set-value Field_文字内容 'CLI + GUI' | Out-Null
    UI invoke TextGenerate | Out-Null
    UI wait-for TextStatus --value '生成完成' --contains -t 10000 | Out-Null
    UI wait-for ResultStats --value '字符' --contains -t 3000 | Out-Null
}
Check 'Desktop generator remains usable' {
    UI invoke NavGenerator | Out-Null
    # Navigation and responsive layout commit asynchronously. Wait for either
    # the visible action or its input flyout button, rather than racing a search.
    $taskReady=[Diagnostics.Stopwatch]::StartNew()
    do {
        $taskGenerate=(UI search GeneratorGenerate --json | ConvertFrom-Json).matches | Where-Object { !$_.isOffscreen } | Select-Object -First 1
        if ($taskGenerate) { break }
        $taskInput=(UI search WorkspaceInputButton --json | ConvertFrom-Json).matches | Where-Object { !$_.isOffscreen } | Select-Object -First 1
        if ($taskInput) { UI invoke WorkspaceInputButton | Out-Null; break }
    } while ($taskReady.ElapsedMilliseconds -lt 5000)
    UI wait-for GeneratorGenerate -t 5000 | Out-Null
    UI invoke GeneratorGenerate | Out-Null
    UI wait-for GeneratorStatus --value '已生成' --contains -t 5000 | Out-Null
}
Check 'ANSI parser remains usable' {
    UI invoke NavAnsi | Out-Null
    UI set-value AnsiInput 'HELLO' | Out-Null
    UI invoke AnsiRender | Out-Null
    UI wait-for AnsiStatus --value '忽略' --contains -t 5000 | Out-Null
    UI set-value AnsiInput 'UPDATED' | Out-Null
    UI wait-for AnsiStatus --value '原文已修改' --contains -t 3000 | Out-Null
}
Check 'Image page retains shared font and source controls' {
    UI invoke NavImage | Out-Null
    UI wait-for ImageSourceInfo -t 5000 | Out-Null
    UI invoke MoreButton | Out-Null
    UI invoke DisplaySettings | Out-Null
    UI wait-for ResultFont -t 3000 | Out-Null
    UI invoke SettingsDone | Out-Null
}
Check 'Desktop settings remain usable' {
    UI invoke SettingsItem | Out-Null
    UI wait-for SettingsTheme -t 5000 | Out-Null
    UI wait-for SettingsAnimations -t 3000 | Out-Null
}
Check 'No desktop application errors' {
    if (Test-Path -LiteralPath (Join-Path $DataDirectory 'errors.log')) { throw 'Desktop error log present.' }
}
$taskResults | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskRoot 'artifacts/ui-shared-services-results.json') -Encoding utf8NoBOM
$taskResults | Format-Table -AutoSize
if ($taskResults.status -contains 'FAIL') { throw 'Shared service desktop regression failed.' }
