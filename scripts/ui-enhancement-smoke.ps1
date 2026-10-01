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
function Select-Choice([string]$Selector,[string]$Value){
 UI invoke $Selector --action expand|Out-Null
 $taskChoice=(UI search $Value --json|ConvertFrom-Json).matches|Where-Object {$_.type -eq 'ListItem' -and $_.name -eq $Value}|Select-Object -First 1
 if(-not $taskChoice){throw "Choice not found: $Value"}
 UI invoke $taskChoice.selector --action select|Out-Null
 UI invoke $Selector --action collapse|Out-Null
}
switch($Module){
 'conversion'{
  Check 'Selected font drives aspect compensation and is saved' {
   UI invoke NavLibrary;UI invoke '打开项目：geometry-fixture';UI wait-for ImageSourceInfo --contains --value '80 × 40' -t 3000
   try{$taskMore=(UI get-property ImageSizeSettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
   if($taskMore){UI invoke MoreButton}
   UI invoke ImageSizeSettings;UI wait-for ImageFontAspect -t 2000;UI invoke ImageFontAspect --action toggle-on
   UI invoke NavText;UI invoke NavImage
   try{$taskMore=(UI get-property DisplaySettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
   if($taskMore){UI invoke MoreButton}
   UI invoke DisplaySettings;UI wait-for ResultFontInput -t 2000
   UI set-value ResultFontInput 'Courier New'
   UI invoke NavText;UI invoke NavImage;UI invoke Button_转换
   Start-Sleep -Milliseconds 1500
   $taskProject=Get-Content (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/recovery.asciiproj') -Raw|ConvertFrom-Json
   if($taskProject.Document.FontFamily -ne 'Courier New'){throw 'Selected result font was not retained'}
   if([Math]::Abs($taskProject.Options.CellAspect-$taskProject.Document.CellWidth/$taskProject.Document.CellHeight) -gt .001){throw 'Measured font ratio does not match conversion'}
   UI screenshot -o (Join-Path $PSScriptRoot '../artifacts/font-compensation.png')
  }
  Check 'Chinese font search and dot style generation' {
   UI invoke NavText;Select-Choice Field_转换方式 '系统字体 → 字符画（支持中文）'
   UI wait-for TextSystemFontInput -t 2000;UI set-value TextSystemFontInput 'Microsoft YaHei UI'
   Select-Choice Field_中文字画风格 '点阵'
   UI set-value Field_文字内容 '测试';UI invoke TextGenerate;UI wait-for ResultStats --contains --value '字符' -t 3000
   $taskText=(UI get-value ResultEditor --json|ConvertFrom-Json).text
   if($taskText -notmatch '●'){throw 'Dot style did not generate dot glyphs'}
  }
 }
 'zoom'{
  Check 'Zoom buttons and reset' {
   UI invoke NavText;UI invoke ResultZoomReset;UI wait-for ResultZoomValue --value '100%' -t 2000
   UI invoke ResultZoomIn;UI wait-for ResultZoomValue --value '110%' -t 2000
   UI invoke ResultZoomOut;UI wait-for ResultZoomValue --value '100%' -t 2000
  }
  Check 'Chinese preview and conversion' {
   Select-Choice Field_转换方式 '系统字体 → 字符画（支持中文）'
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
