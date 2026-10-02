param([int]$AppPid,[switch]$Prepare,[ValidateSet('initial','restart')][string]$Phase='initial')
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskData=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/alpha3-ui-data'
if($Prepare){
 New-Item -ItemType Directory -Force $taskData|Out-Null
 $taskRecoveryDirectory=Join-Path $taskData 'recovery'
 if(Test-Path -LiteralPath $taskRecoveryDirectory){Get-ChildItem -LiteralPath $taskRecoveryDirectory -Filter '*.asciiproj'|ForEach-Object {Remove-Item -LiteralPath $_.FullName}}
 if(Test-Path (Join-Path $taskData 'errors.log')){Remove-Item -LiteralPath (Join-Path $taskData 'errors.log')}
 foreach($taskName in @('one','two')){
  $taskText=if($taskName -eq 'one'){'first'}else{'second'}
  @{Version=1;Document=@{Width=$taskText.Length;Height=1;Text=$taskText;Title="session-$taskName"};Mode='text';SourceText="$taskText source";Options=@{Columns=120}}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $taskData "session-$taskName.asciiproj") -Encoding utf8
 }
 @{RecentFiles=@((Join-Path $taskData 'session-one.asciiproj'),(Join-Path $taskData 'session-two.asciiproj'))}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $taskData 'settings.json') -Encoding utf8
 @{Tabs=@();ActiveId=$null;Recoveries=@()}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $taskData 'session.json') -Encoding utf8
 return
}
if(!$AppPid){throw 'Provide the isolated test app PID.'}
$taskHwnd=(& winapp ui list-windows -a $AppPid --json|ConvertFrom-Json|Where-Object ownerHwnd -eq 0|Select-Object -First 1).hwnd
function UI {
 $taskOutput=& winapp ui @args -w $taskHwnd 2>&1
 if($LASTEXITCODE -ne 0){throw ($taskOutput -join "`n")}
 if($args[0] -eq 'invoke' -and $args[1] -like 'Nav*'){Start-Sleep -Milliseconds 250}
 $taskOutput
}
function State {Get-Content (Join-Path $taskData 'session.json') -Raw|ConvertFrom-Json}
function Tab([string]$Title){
 $taskEntry=(State).Tabs|Where-Object Title -eq $Title|Select-Object -First 1
 if(!$taskEntry){throw "Missing session: $Title"}
 UI invoke ('ProjectTab_'+$taskEntry.Id.Substring(0,8)) --action select|Out-Null
}
function Count([int]$Expected){
 Start-Sleep -Milliseconds 300
 if(@((State).Tabs).Count -ne $Expected){throw "Expected $Expected independent tabs"}
}
function CloseTab([string]$Title){
 $taskEntry=(State).Tabs|Where-Object Title -eq $Title|Select-Object -First 1
 $taskClose=(UI search '关闭标签页' --root ('ProjectTab_'+$taskEntry.Id.Substring(0,8)) --json|ConvertFrom-Json).matches|Select-Object -First 1
 if(!$taskClose){throw 'Tab close button missing'}
 UI invoke $taskClose.selector|Out-Null
}
if($Phase -eq 'initial'){
 UI invoke NavLibrary|Out-Null;UI invoke '打开项目：session-one'|Out-Null
 UI wait-for Field_文字内容 --value 'first source' -t 5000|Out-Null;Count 1
 UI invoke NavHome|Out-Null;UI invoke NavLibrary|Out-Null;UI invoke '打开项目：session-one'|Out-Null;Count 1
 UI invoke NavHome|Out-Null;UI invoke NavLibrary|Out-Null;UI invoke '打开项目：session-two'|Out-Null
 UI wait-for Field_文字内容 --value 'second source' -t 5000|Out-Null;Count 2
 Tab 'session-one';UI wait-for Field_文字内容 --value 'first source' -t 3000|Out-Null
 UI set-value ResultEditor 'changed first'|Out-Null;Tab 'session-two'
 UI wait-for ResultEditor --value 'second' -t 3000|Out-Null
 $taskOne=(State).Tabs|Where-Object Title -eq 'session-one'
 Start-Sleep -Milliseconds 300
 $taskRecovery=Get-Content (Join-Path $taskData "recovery/$($taskOne.Id).asciiproj") -Raw|ConvertFrom-Json
 if($taskRecovery.Document.Text -ne 'changed first'){throw 'Switching tabs lost pending recovery'}
 UI set-value ResultEditor 'saved second'|Out-Null;UI invoke Button_保存项目|Out-Null
 Start-Sleep -Milliseconds 600
 $taskSaved=Get-Content (Join-Path $taskData 'session-two.asciiproj') -Raw|ConvertFrom-Json
 $taskBackup=Get-Content (Join-Path $taskData 'session-two.asciiproj.bak') -Raw|ConvertFrom-Json
 if($taskSaved.Version -ne 4 -or $taskSaved.Document.Text -ne 'saved second' -or $taskBackup.Version -ne 1){throw 'Migration save or backup failed'}
 Tab 'session-one';CloseTab 'session-one'
 UI wait-for SecondaryButton -t 3000|Out-Null;UI invoke 取消|Out-Null;Count 2
 CloseTab 'session-one'
 UI wait-for '保留恢复' -t 3000|Out-Null;UI invoke 保留恢复|Out-Null;Count 1
 if(@((State).Recoveries).Count -ne 1){throw 'Closed recovery not indexed'}
 UI invoke NavHome|Out-Null;UI wait-for '恢复：session-one' -t 3000|Out-Null;UI invoke '恢复：session-one'|Out-Null
 UI wait-for ResultEditor --value 'changed first' -t 3000|Out-Null;Count 2
 UI invoke NavText|Out-Null;UI set-value Field_文字内容 'draft without generation'|Out-Null
 UI invoke NavHome|Out-Null;Count 3
 $taskDraft=(State).Tabs|Where-Object Path -eq $null|Select-Object -Last 1
 $taskDraftSnapshot=Get-Content (Join-Path $taskData "recovery/$($taskDraft.Id).asciiproj") -Raw|ConvertFrom-Json
 if($taskDraftSnapshot.SourceText -ne 'draft without generation'){throw 'Input draft was lost'}
}else{
 UI wait-for ProjectTabs -t 5000|Out-Null;Start-Sleep -Milliseconds 600;Count 3
 $taskDraft=(State).Tabs|Where-Object Path -eq $null|Select-Object -Last 1
 UI invoke ('ProjectTab_'+$taskDraft.Id.Substring(0,8)) --action select|Out-Null
 UI wait-for Field_文字内容 --value 'draft without generation' -t 4000|Out-Null
 Tab 'session-one';UI wait-for ResultEditor --value 'changed first' -t 4000|Out-Null
 UI wait-for Field_文字内容 --value 'first source' -t 3000|Out-Null
 Tab 'session-two';UI wait-for ResultEditor --value 'saved second' -t 3000|Out-Null
 UI invoke Close|Out-Null
 UI wait-for '保留恢复并退出' -t 3000|Out-Null;UI invoke '保留恢复并退出'|Out-Null
}
Write-Output "PASS project sessions $Phase"


