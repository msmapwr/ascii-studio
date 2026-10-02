param([int]$AppPid, [switch]$Prepare)
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$taskData = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/alpha4-ui-data'
$taskProject = Join-Path $taskData 'unicode.asciiproj'
if ($Prepare) {
    New-Item -ItemType Directory -Force $taskData | Out-Null
    $taskErrorLog = Join-Path $taskData 'errors.log'
    if (Test-Path -LiteralPath $taskErrorLog) { Remove-Item -LiteralPath $taskErrorLog }
    @{Version=2;Document=@{Width=2;Height=1;Text='测试';Title='unicode';Colors=@(4294901760,4278255360);BackgroundColors=@(4278190335,4278190335)};Mode='text';SourceText='source'} | ConvertTo-Json -Depth 8 | Set-Content $taskProject -Encoding utf8
    @{RecentFiles=@($taskProject)} | ConvertTo-Json | Set-Content (Join-Path $taskData 'settings.json') -Encoding utf8
    @{Tabs=@();ActiveId=$null;Recoveries=@()} | ConvertTo-Json | Set-Content (Join-Path $taskData 'session.json') -Encoding utf8
    return
}
if (!$AppPid) { throw 'Provide the isolated test app PID.' }
$taskHwnd = (& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json | Where-Object ownerHwnd -eq 0 | Select-Object -First 1).hwnd
function UI {
    $taskOutput = & winapp ui @args -w $taskHwnd 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($taskOutput -join "`n") }
    if ($args[0] -eq 'invoke' -and $args[1] -like 'Nav*') { Start-Sleep -Milliseconds 250 }
    $taskOutput
}
UI invoke NavLibrary | Out-Null
UI invoke '打开项目：unicode' | Out-Null
UI wait-for ResultEditor --value '测试' -t 5000 | Out-Null
UI invoke Button_保存项目 | Out-Null
Start-Sleep -Milliseconds 600
$taskSaved = Get-Content $taskProject -Raw | ConvertFrom-Json
$taskBackup = Get-Content ($taskProject + '.bak') -Raw | ConvertFrom-Json
if ($taskSaved.Version -ne 3 -or $taskSaved.Document.GridVersion -ne 1 -or $taskSaved.Document.Width -ne 4 -or $taskBackup.Version -ne 2) { throw 'Legacy Unicode project migration failed' }
if (($taskSaved.Document.Colors -join ',') -ne '4294901760,4294901760,4278255360,4278255360') { throw 'Wide foreground colors not migrated' }
UI invoke MoreButton | Out-Null
UI invoke DisplaySettings | Out-Null
UI invoke ResultImagePreview --action toggle-on | Out-Null
Start-Sleep -Milliseconds 600
UI invoke ResultImagePreview --action toggle-off | Out-Null
UI invoke NavHome | Out-Null
$taskTab = (Get-Content (Join-Path $taskData 'session.json') -Raw | ConvertFrom-Json).Tabs | Where-Object Title -eq 'unicode' | Select-Object -First 1
UI invoke ('ProjectTab_' + $taskTab.Id.Substring(0,8)) --action select | Out-Null
UI set-value ResultEditor '中é😀!' | Out-Null
UI invoke Button_保存项目 | Out-Null
Start-Sleep -Milliseconds 600
$taskSaved = Get-Content $taskProject -Raw | ConvertFrom-Json
if ($taskSaved.Document.Width -ne 6 -or $taskSaved.Document.Text -ne '中é😀!') { throw 'Mixed Unicode editor dimensions wrong' }
UI invoke NavAnsi | Out-Null
UI set-value AnsiInput "`e[31m中é😀!" | Out-Null
UI invoke AnsiRender | Out-Null
Start-Sleep -Milliseconds 500
UI invoke MoreButton | Out-Null
UI invoke DisplaySettings | Out-Null
UI invoke ResultImagePreview --action toggle-off | Out-Null
UI invoke NavHome | Out-Null
$taskAnsiTab = (Get-Content (Join-Path $taskData 'session.json') -Raw | ConvertFrom-Json).Tabs | Where-Object Mode -eq 'ansi' | Select-Object -Last 1
UI invoke ('ProjectTab_' + $taskAnsiTab.Id.Substring(0,8)) --action select | Out-Null
UI wait-for ResultEditor --value "中é😀!$((' ' * 74))" -t 5000 | Out-Null
if (Test-Path -LiteralPath (Join-Path $taskData 'errors.log')) { throw 'Unicode UI recorded an application error; inspect the isolated log' }
Write-Output 'PASS Unicode migration, preview, editor, and ANSI UI'
