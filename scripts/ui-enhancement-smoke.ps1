param([Parameter(Mandatory)][int]$AppPid,[ValidateSet('zoom','conversion','crypto','assist')][string]$Module='zoom')
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskHwnd=(& winapp ui list-windows -a $AppPid --json|ConvertFrom-Json|Where-Object ownerHwnd -eq 0|Select-Object -First 1).hwnd
$taskResults=[System.Collections.Generic.List[object]]::new()
function UI {
 if($args[0] -eq 'invoke' -and ($args[1] -like 'Nav*' -or $args[1] -eq 'SettingsItem')){
  & winapp ui get-property $args[1] -w $taskHwnd --json 2>&1|Out-Null
  if($LASTEXITCODE -ne 0){& winapp ui invoke TogglePaneButton -w $taskHwnd|Out-Null}
 }
 $taskResponse=& winapp ui @args -w $taskHwnd 2>&1
 if($LASTEXITCODE -ne 0){throw ($taskResponse -join "`n")}
 if($args[0] -eq 'invoke' -and ($args[1] -like 'Nav*' -or $args[1] -eq 'SettingsItem')){Start-Sleep -Milliseconds 250}
 $taskResponse
}
function Check([string]$Name,[scriptblock]$Action){
 try{& $Action|Out-Null;$taskResults.Add(@{name=$Name;status='PASS'})}
 catch{$taskResults.Add(@{name=$Name;status='FAIL';detail=$_.Exception.Message})}
}
switch($Module){
 'zoom'{
  Check 'Zoom buttons and reset' {
   UI invoke NavText;UI invoke ResultZoomReset;UI wait-for ResultZoomValue --value '100%' -t 2000
   UI invoke ResultZoomIn;UI wait-for ResultZoomValue --value '110%' -t 2000
   UI invoke ResultZoomOut;UI wait-for ResultZoomValue --value '100%' -t 2000
  }
  Check 'Chinese preview and conversion' {
   UI send-keys 'esc home down' --target Field_转换方式 --via send-input
   UI wait-for FontPreviewSample --value '测试' -t 3000
   UI set-value Field_文字内容 '测试';UI invoke TextGenerate
   UI wait-for ResultStats --value '字符' --contains -t 5000
   $taskText=(UI get-value ResultEditor --json|ConvertFrom-Json).text
   if([string]::IsNullOrWhiteSpace($taskText)){throw 'Chinese result is blank'}
   UI screenshot -o (Join-Path $PSScriptRoot '../artifacts/chinese-preview.png')
  }
 }
}
$taskOutput=Join-Path $PSScriptRoot "../artifacts/smoke-$Module.json"
$taskResults|ConvertTo-Json -Depth 5|Set-Content $taskOutput -Encoding utf8
$taskResults|ForEach-Object {Write-Output ($_|ConvertTo-Json -Compress)}
if(@($taskResults|Where-Object status -eq 'FAIL').Count){exit 1}
