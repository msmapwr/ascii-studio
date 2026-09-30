param([Parameter(Mandatory)][int]$AppPid,[string]$Only)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot 'artifacts/ui-polish'
$taskData=Join-Path $taskRoot 'artifacts/polish-test-data'
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskResults=[System.Collections.Generic.List[object]]::new()
$taskWindows=& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
$taskHwnd=($taskWindows|Where-Object ownerHwnd -eq 0|Select-Object -First 1).hwnd
if(-not $taskHwnd){throw 'Test window not ready.'}
# Allow the initial queued navigation and WinUI template loading to settle.
Start-Sleep -Milliseconds 1200
function UI {
 $taskResponse=& winapp ui @args -w $taskHwnd 2>&1
 if($LASTEXITCODE -ne 0){throw ($taskResponse -join "`n")}
 if($args[0] -eq 'invoke' -and ($args[1] -like 'Nav*' -or $args[1] -eq 'SettingsItem')){Start-Sleep -Milliseconds 250}
 $taskResponse
}
function Check([string]$Name,[scriptblock]$Action) {
 if($Only -and $Name -notlike "*$Only*"){return}
 try{& $Action|Out-Null;$taskResults.Add([pscustomobject]@{name=$Name;status='PASS'})}
 catch{$taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message})}
}
function Choose([string]$Id,[string]$Value) {
 UI invoke $Id --action expand|Out-Null
 $taskItems=(UI search $Value --type ListItem --json|ConvertFrom-Json).matches
 $taskItem=$taskItems|Where-Object {$_.name -eq $Value -and -not $_.isOffscreen}|Select-Object -First 1
 if(-not $taskItem){throw "Choice $Value not found in $Id."}
 UI invoke $taskItem.selector --action select|Out-Null
 UI wait-for $Id --value $Value -t 2000|Out-Null
}
function Setting([string]$Name,$Value) {
 for($taskTry=0;$taskTry -lt 30;$taskTry++){
  $taskPrefs=Get-Content (Join-Path $taskData 'settings.json') -Raw -ErrorAction SilentlyContinue|ConvertFrom-Json
  if($taskPrefs.$Name -eq $Value){return}
  Start-Sleep -Milliseconds 100
 }
 throw "Setting $Name did not persist as $Value."
}
function Open-Settings {
 UI invoke SettingsItem|Out-Null;UI wait-for SettingsTheme -t 3000|Out-Null
}
function Toolbar([string]$Name,[string]$Field) {
 try{$taskElement=(UI get-property $Name --json|ConvertFrom-Json).element;$taskMore=$taskElement.isOffscreen}catch{$taskMore=$true}
 if($taskMore){UI invoke MoreButton|Out-Null}
 UI invoke $Name|Out-Null;UI wait-for $Field -t 2000|Out-Null
}
Check 'FIGlet example and font search render abc' {
 UI invoke NavText;UI wait-for TextFontSearchInput -t 3000
 UI wait-for FontPreviewSample --value abc -t 3000
 UI wait-for FontPreviewArt --value '_' --contains -t 5000
 $taskBefore=(UI get-value FontPreviewArt --json|ConvertFrom-Json).text
 UI set-value TextFontSearchInput Slant
 for($taskTry=0;$taskTry -lt 30;$taskTry++){
  $taskAfter=(UI get-value FontPreviewArt --json|ConvertFrom-Json).text
  if($taskAfter -and $taskBefore -ne $taskAfter){break}
  Start-Sleep -Milliseconds 100
 }
 @{before=$taskBefore;after=$taskAfter;search=(UI get-value TextFontSearchInput --json|ConvertFrom-Json).text}|ConvertTo-Json|Set-Content (Join-Path $taskOutput 'font-change.json')
 if(-not $taskAfter -or $taskBefore -eq $taskAfter){throw 'Preview did not change with font.'}
 UI wait-for TextFontSearchInput --value Slant -t 2000
 UI screenshot -o (Join-Path $taskOutput 'font-preview.png')
}
Check 'Font preview preserves edited main result' {
 UI invoke TextGenerate;UI wait-for ResultStats --value '字符' --contains -t 10000
 UI set-value ResultEditor 'keep this result'
 UI set-value TextFontSearchInput Standard
 Start-Sleep -Milliseconds 350
 UI wait-for ResultEditor --value 'keep this result' -t 2000
}
Check 'Chinese system font preview uses 测试' {
 Choose Field_转换方式 '系统字体 → 字符画（支持中文）'
 UI wait-for TextSystemFont -p IsOffscreen --value False -t 3000
 UI wait-for FontPreviewSample --value 测试 -t 3000
 $taskArt=(UI get-value FontPreviewArt --json|ConvertFrom-Json).text
 if(-not $taskArt){throw 'Chinese converted preview is empty.'}
 Choose TextSystemFont SimSun
 UI wait-for TextSystemFont --value SimSun -t 2000
 UI screenshot -o (Join-Path $taskOutput 'chinese-preview.png')
}
Check 'Settings include all categories' {
 Open-Settings
 foreach($taskId in @('SettingsAnimations','SettingsCompact','SettingsRememberWindow','SettingsFontSize','SettingsWordWrap','SettingsShowStats','SettingsAutoConvert','SettingsDelay','SettingsColumns','SettingsExportFormat','SettingsExportScale','SettingsFilePrefix','SettingsReset')){UI wait-for $taskId -t 2000}
 UI screenshot -o (Join-Path $taskOutput 'settings.png')
}
Check 'Light and dark themes settle after animation' {
 Choose SettingsTheme 浅色;Setting Theme Light;Start-Sleep -Milliseconds 350;UI screenshot -o (Join-Path $taskOutput 'light.png')
 Choose SettingsTheme 深色;Setting Theme Dark;Start-Sleep -Milliseconds 350;UI screenshot -o (Join-Path $taskOutput 'dark.png')
 $taskLight=[System.Drawing.Bitmap]::new((Join-Path $taskOutput 'light.png'));$taskDark=[System.Drawing.Bitmap]::new((Join-Path $taskOutput 'dark.png'))
 try{if($taskLight.GetPixel(20,100).R -le $taskDark.GetPixel(20,100).R){throw 'Theme did not change visible background.'}}finally{$taskLight.Dispose();$taskDark.Dispose()}
}
Check 'Animation, compact layout and window preference persist' {
 UI invoke SettingsAnimations --action toggle-off;Setting Animations $false
 UI invoke SettingsCompact --action toggle-on;Setting CompactLayout $true
 UI invoke SettingsRememberWindow --action toggle-off;Setting RememberWindow $false
}
Check 'Editing defaults apply to existing result' {
 UI set-value SettingsFontSizeInput 17;UI focus SettingsTheme;Setting PreviewFontSize 17
 UI invoke SettingsWordWrap --action toggle-on;Setting WordWrap $true
 UI invoke NavText;Toolbar DisplaySettings Field_字号同时用于图片导出
 UI wait-for Field_字号同时用于图片导出Input --value 17 -t 2000
}
Check 'Conversion and export defaults persist' {
 Open-Settings
 UI invoke SettingsAutoConvert --action toggle-off;Setting AutoConvert $false
 UI set-value SettingsDelayInput 300;UI focus SettingsTheme;Setting ConversionDelay 300
 UI set-value SettingsColumnsInput 240;UI focus SettingsTheme;Setting DefaultColumns 240
 Choose SettingsExportFormat PNG;Setting DefaultExportFormat PNG
 Choose SettingsExportScale '2×';Setting ExportScale 2
}
Check 'Image uses configured default resolution and manual action' {
 UI invoke NavImage;UI wait-for ImageResolution -t 3000
 Toolbar ImageSizeSettings ImageColumns;UI wait-for ImageColumnsInput --value 240 -t 2000
 UI invoke NavText;UI invoke NavImage;UI invoke Button_转换
 UI wait-for '请先选择图片。' -t 3000
}
Check 'Export default is applied' {
 UI invoke NavText;Toolbar ExportSettings Field_导出格式
 UI wait-for Field_导出格式 --value PNG -t 2000;UI wait-for ExportScale --value '2×' -t 2000
}
Check 'Secondary settings can be edited and retained' {
 UI invoke NavImage;Toolbar ImageSizeSettings ImageColumnsInput
 UI set-value ImageColumnsInput 180;UI focus ImageAutoRows
 UI invoke NavText;UI invoke NavImage;Toolbar ImageSizeSettings ImageColumnsInput
 UI wait-for ImageColumnsInput --value 180 -t 2000
 UI invoke NavText;UI invoke NavImage;Toolbar ImageAdjustmentSettings ImageBrightness
 UI set-value ImageBrightness 1.5;UI wait-for ImageBrightnessValue --value 1.5 -t 2000
 UI invoke NavText;UI invoke NavImage;Toolbar ImageAdjustmentSettings ImageBrightness
 UI wait-for ImageBrightnessValue --value 1.5 -t 2000
 UI invoke NavText;Toolbar ExportSettings Field_导出格式
 Choose Field_导出格式 SVG;Choose ExportScale '3×'
 UI invoke NavImage;UI invoke NavText;Toolbar ExportSettings Field_导出格式
 UI wait-for Field_导出格式 --value SVG -t 2000;UI wait-for ExportScale --value '3×' -t 2000
 UI screenshot -o (Join-Path $taskOutput 'secondary-settings.png')
}
Check 'Reset restores defaults through confirmation' {
 Open-Settings;UI invoke SettingsReset;UI wait-for PrimaryButton -t 3000;UI invoke PrimaryButton
 Setting PreviewFontSize 13;Setting Animations $true;Setting CompactLayout $false;Setting WordWrap $false;Setting AutoConvert $true;Setting DefaultExportFormat TXT;Setting ExportScale 1
}
$taskResults|ConvertTo-Json -Depth 5|Set-Content (Join-Path $taskOutput 'results.json') -Encoding utf8
$taskResults|Format-Table name,status,detail -AutoSize
if(@($taskResults|Where-Object status -eq 'FAIL').Count -gt 0){exit 1}

