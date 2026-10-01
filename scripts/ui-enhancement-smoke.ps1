param([Parameter(Mandatory)][int]$AppPid,[ValidateSet('zoom','conversion','crypto','assist','ansi','generator','history')][string]$Module='zoom')
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
 'history'{
  Check 'Manual edits undo redo and divergent changes' {
   UI invoke NavGenerator;UI set-value Field_边框内容 'history source';UI invoke GeneratorGenerate
   UI wait-for ResultEditor --contains --value 'history source' -t 4000
   Start-Sleep -Milliseconds 600
   UI set-value ResultEditor 'edited history';Start-Sleep -Milliseconds 600
   UI invoke ResultUndo;UI wait-for ResultEditor --contains --value 'history source' -t 4000
   UI invoke ResultRedo;UI wait-for ResultEditor --value 'edited history' -t 4000
   UI invoke ResultUndo;UI set-value ResultEditor 'divergent history';Start-Sleep -Milliseconds 600
   UI wait-for ResultRedo -p IsEnabled --value False -t 2000
  }
  Check 'Generated candidate protects edits and undo restores source' {
   UI set-value Field_边框内容 'new source';UI invoke GeneratorGenerate
   UI wait-for ResultProtection -p IsOffscreen --value False -t 4000
   UI wait-for ResultEditor --value 'divergent history' -t 2000
   UI invoke ResultAcceptCandidate;UI wait-for ResultEditor --contains --value 'new source' -t 4000
   UI invoke ResultUndo;UI wait-for ResultEditor --value 'divergent history' -t 4000
   UI wait-for Field_边框内容 --value 'history source' -t 2000
   UI invoke ResultRedo;UI wait-for ResultEditor --contains --value 'new source' -t 4000
   Start-Sleep -Milliseconds 1400
   $taskProject=Get-Content (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/recovery.asciiproj') -Raw|ConvertFrom-Json
   if($taskProject.SourceText -ne 'new source'){throw 'History restored stale source'}
   if(-not (Get-ChildItem (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/recovery') -Filter '*.asciiproj')){throw 'Independent recovery file missing'}
  }
  Check 'Recovered manual edits remain protected and retain original preview' {
   UI set-value ResultEditor 'protected after reopen';Start-Sleep -Milliseconds 1400
   UI invoke NavHome;UI invoke Button_恢复最近一次结果
   UI wait-for ResultEditor --value 'protected after reopen' -t 4000
   UI invoke GeneratorGenerate;UI wait-for ResultAcceptCandidate -t 4000
   UI wait-for ResultEditor --value 'protected after reopen' -t 2000
   UI invoke ResultAcceptCandidate;UI wait-for ResultEditor --contains --value 'new source' -t 4000
   UI invoke NavAnsi;UI invoke Button_载入彩色示例;UI wait-for AnsiStatus --contains --value '80 × 6' -t 4000
   UI invoke DisplaySettings;UI invoke ResultImagePreview --action toggle-off
   UI invoke NavGenerator;UI invoke NavAnsi
   UI set-value ResultEditor 'color edit';Start-Sleep -Milliseconds 600;UI invoke ResultUndo
   Start-Sleep -Milliseconds 1400
   $taskColor=Get-Content (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/recovery.asciiproj') -Raw|ConvertFrom-Json
   if($taskColor.Document.BackgroundColors[80] -ne 4278190250 -or $taskColor.Document.Colors.Count -ne 480){throw 'Undo lost ANSI colors'}
  }
 }
 'generator'{
  Check 'Generator source and edited result recover independently' {
   UI invoke NavGenerator;UI set-value Field_边框内容 'project source';UI invoke GeneratorGenerate
   UI wait-for ResultEditor --contains --value 'project source' -t 4000
   UI set-value ResultEditor 'manually edited result'
   Start-Sleep -Milliseconds 1400
   $taskProject=Get-Content (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/recovery.asciiproj') -Raw|ConvertFrom-Json
   if($taskProject.Mode -ne 'generator' -or $taskProject.SourceText -ne 'project source' -or $taskProject.Document.Text -ne 'manually edited result'){throw 'Generator source or manual result was not saved'}
   UI invoke NavHome;UI invoke Button_恢复最近一次结果
   UI wait-for GeneratorStatus --contains --value '项目已恢复' -t 5000
   Start-Sleep -Milliseconds 400
   UI wait-for ResultEditor --value 'manually edited result' -t 2000
   UI invoke GeneratorGenerate;UI wait-for ResultAcceptCandidate -t 4000;UI invoke ResultAcceptCandidate
   UI wait-for ResultEditor --contains --value 'project source' -t 4000
  }
  Check 'Generator automatic conversion and reset follow preferences' {
   UI invoke SettingsItem;UI invoke SettingsAutoConvert --action toggle-on
   UI invoke NavGenerator;UI set-value Field_边框内容 'automatic generator'
   UI wait-for ResultEditor --contains --value 'automatic generator' -t 4000
   UI invoke SettingsItem;UI invoke SettingsAutoConvert --action toggle-off
   UI invoke NavGenerator;UI set-value Field_边框内容 'manual generator'
   Start-Sleep -Milliseconds 400
   UI wait-for ResultEditor --contains --value 'automatic generator' -t 2000
   UI invoke GeneratorSettings;UI invoke Button_恢复生成器默认参数
   UI invoke NavHome;UI invoke NavGenerator
   UI wait-for Field_边框内容 --value 'Hello, ASCII!' -t 2000
   UI wait-for ResultEditor --contains --value 'automatic generator' -t 2000
   UI invoke GeneratorGenerate;UI wait-for ResultEditor --contains --value 'Hello, ASCII!' -t 4000
   UI invoke SettingsItem;UI invoke SettingsAutoConvert --action toggle-on
  }
 }
 'ansi'{
  Check 'ANSI colors and recovery retain original parameters' {
   UI invoke NavAnsi;UI invoke Button_载入彩色示例
   UI wait-for AnsiStatus --contains --value '80 × 6' -t 5000
   Start-Sleep -Milliseconds 1200
   $taskProject=Get-Content (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/recovery.asciiproj') -Raw|ConvertFrom-Json
   if($taskProject.Mode -ne 'ansi' -or $taskProject.Document.BackgroundColors[80] -ne 4278190250 -or $taskProject.SourceText -notmatch 'ASCII STUDIO'){throw 'ANSI colors or source were not saved'}
   UI screenshot -o (Join-Path $PSScriptRoot '../artifacts/ansi-colors.png')
   UI invoke NavHome;UI invoke Button_恢复最近一次结果
   UI wait-for AnsiStatus --contains --value '项目已恢复' -t 5000
   UI invoke AnsiRender;UI wait-for AnsiStatus --contains --value '80 × 6' -t 5000
  }
  Check 'Unknown commands are reported and invalid input preserves result' {
   UI set-value AnsiInput "abc$([char]27)[?25l";UI invoke AnsiRender
   UI wait-for AnsiStatus --contains --value '忽略 1' -t 4000
   UI invoke DisplaySettings;UI invoke ResultImagePreview --action toggle-off
   $taskBefore=(UI get-value ResultEditor --json|ConvertFrom-Json).text
   UI invoke NavHome;UI invoke NavAnsi
   UI set-value AnsiInput "$([char]27)[2001;1Hx";UI invoke AnsiRender
   Start-Sleep -Milliseconds 500
   $taskAfter=(UI get-value ResultEditor --json|ConvertFrom-Json).text
   if($taskBefore -ne $taskAfter){throw 'Parser error replaced the previous document'}
  }
 }
 'assist'{
  Check 'Comment preview replace and restore original' {
   UI invoke NavText;UI set-value ResultEditor '  abc'
   try{$taskMore=(UI get-property CommentSettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
   if($taskMore){UI invoke MoreButton}
   UI invoke CommentSettings;UI invoke Button_生成注释预览
   UI wait-for CommentPreview --contains --value '//   abc' -t 2000
   UI invoke Button_替换结果;UI wait-for PrimaryButton -t 2000;UI invoke PrimaryButton
   UI wait-for ResultEditor --contains --value '//   abc' -t 2000
   UI invoke NavCrypto;UI invoke NavText
   try{$taskMore=(UI get-property CommentSettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
   if($taskMore){UI invoke MoreButton}
   UI invoke CommentSettings;UI invoke Button_恢复注释前原文
   UI wait-for ResultEditor --contains --value '  abc' -t 2000
  }
  Check 'Tutorial beginner help and UI font preferences' {
   UI invoke NavTutorial;UI invoke TutorialBeginner --action toggle-on
   UI wait-for Help_新手模式 -p IsOffscreen --value False -t 2000
   UI invoke SettingsItem;UI wait-for SettingsUiFontInput -t 2000
   UI set-value SettingsUiFontInput 'Arial';UI set-value SettingsUiFontSize '16'
   Start-Sleep -Milliseconds 500
   $taskSettings=Get-Content (Join-Path $PSScriptRoot '../artifacts/enhancement-test-data/settings.json') -Raw|ConvertFrom-Json
   if(-not $taskSettings.BeginnerMode -or $taskSettings.UiFontFamily -ne 'Arial' -or $taskSettings.UiFontSize -ne 16){throw 'UI preferences were not saved'}
   UI set-value SettingsUiFontInput 'Segoe UI';UI set-value SettingsUiFontSize '14'
   UI invoke NavTutorial;UI invoke TutorialBeginner --action toggle-off
   UI invoke NavHome;UI screenshot -o (Join-Path $PSScriptRoot '../artifacts/home-alignment.png')
  }
 }
 'crypto'{
  Check 'Digest generation and irreversible mode' {
   UI invoke NavCrypto;Select-Choice CryptoCategory '不可逆摘要'
   UI set-value CryptoInput 'abc';UI invoke CryptoEncrypt
   UI wait-for CryptoOutput --value 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad' -t 3000
   UI wait-for CryptoDecrypt -p IsEnabled --value False -t 2000
  }
  Check 'Encoding round trip and failure preserves result' {
   Select-Choice CryptoCategory '编码';UI set-value CryptoInput 'abc';UI invoke CryptoEncrypt
   UI wait-for CryptoOutput --value 'YWJj' -t 2000
   UI set-value CryptoInput 'YWJj';UI invoke CryptoDecrypt;UI wait-for CryptoOutput --value 'abc' -t 2000
   UI set-value CryptoInput '!!!';UI invoke CryptoDecrypt;UI wait-for CryptoOutput --value 'abc' -t 2000
  }
 }
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
