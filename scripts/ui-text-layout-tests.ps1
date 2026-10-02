param([int]$AppPid,[switch]$Prepare)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskData=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/alpha7-ui-data'
$taskProject=Join-Path $taskData 'text.asciiproj'
if($Prepare){
 New-Item -ItemType Directory -Force $taskData|Out-Null
 @{Version=4;Document=@{GridVersion=1;Width=7;Height=1;Text='fixture';Title='text'};Mode='text';SourceText='Hello world';Options=@{Columns=120};Parameters=@{mode='0';font='Standard'}}|ConvertTo-Json -Depth 8|Set-Content $taskProject -Encoding utf8
 @{Version=4;Document=@{GridVersion=1;Width=7;Height=1;Text='missing';Title='missing'};Mode='text';SourceText='ABC';Parameters=@{mode='0';font=('user:'+('a'*64))}}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $taskData 'missing.asciiproj') -Encoding utf8
 @{RecentFiles=@($taskProject,(Join-Path $taskData 'missing.asciiproj'));AutoConvert=$false;Animations=$false}|ConvertTo-Json|Set-Content (Join-Path $taskData 'settings.json') -Encoding utf8
 @{Tabs=@();ActiveId=$null;Recoveries=@()}|ConvertTo-Json|Set-Content (Join-Path $taskData 'session.json') -Encoding utf8
 $taskLog=Join-Path $taskData 'errors.log';if(Test-Path $taskLog){Remove-Item -LiteralPath $taskLog}
 $taskFavorites=Join-Path $taskData 'fonts/favorites.json';if(Test-Path $taskFavorites){Remove-Item -LiteralPath $taskFavorites}
 return
}
if(!$AppPid){throw 'Provide isolated app PID.'}
$env:WINAPP_UI_WORKFLOW_ID=[Guid]::NewGuid().ToString()
$taskHwnd=(& winapp ui list-windows -a $AppPid --json|ConvertFrom-Json|Where-Object ownerHwnd -eq 0|Select-Object -First 1).hwnd
$taskCli=(Get-AppxPackage -Name winapp).InstallLocation+'\winapp.exe'
function UI{
 $taskStart=[Diagnostics.ProcessStartInfo]::new($taskCli);$taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
 foreach($a in (@('ui')+$args+@('-w',"$taskHwnd"))){$taskStart.ArgumentList.Add("$a")}
 $taskProcess=[Diagnostics.Process]::Start($taskStart)
 try{$taskOutput=$taskProcess.StandardOutput.ReadToEnd();$taskError=$taskProcess.StandardError.ReadToEnd();$taskProcess.WaitForExit();if($taskProcess.ExitCode){throw "$($args -join ' '): $taskOutput $taskError"};$taskOutput}finally{$taskProcess.Dispose()}
}
function Settings([string]$id){UI invoke MoreButton|Out-Null;UI invoke $id|Out-Null}
function Done{UI invoke SettingsDone|Out-Null}
function Choose([string]$id,[string]$value){UI invoke $id|Out-Null;$choice=(UI search $value --json|ConvertFrom-Json).matches|Where-Object{$_.type -eq 'ListItem'-and $_.name -eq $value}|Select-Object -First 1;if(!$choice){throw "Missing $value"};UI invoke $choice.selector --action select|Out-Null}
function Generate{UI invoke TextGenerate|Out-Null;UI wait-for TextStatus --value '生成完成' -t 10000|Out-Null;Start-Sleep -Milliseconds 300;UI invoke Button_保存项目|Out-Null;Start-Sleep -Milliseconds 300;Get-Content $taskProject -Raw|ConvertFrom-Json}
UI invoke NavLibrary|Out-Null;UI invoke '打开项目：text'|Out-Null
UI wait-for Field_文字内容 --value 'Hello world' -t 5000|Out-Null
UI set-value Field_文字内容 'AA'|Out-Null
Settings TextLayoutSettings;Choose TextHorizontal '完整宽度';Done;$full=Generate
Settings TextLayoutSettings;Choose TextHorizontal '挤紧';Done;$kern=Generate
if($full.Document.Width -le $kern.Document.Width){throw 'Horizontal packing did not change width'}
Settings TextLayoutSettings;UI set-value TextMaximumWidthInput '30'|Out-Null;UI invoke TextWrap --action toggle-on|Out-Null;Choose TextAlignment '居中';UI set-value TextLineSpacingInput '1'|Out-Null;Done
UI set-value Field_文字内容 'Hello world longword'|Out-Null;$wrapped=Generate
if($wrapped.Document.Width -ne 30 -or $wrapped.Document.Height -le $kern.Document.Height){throw 'Wrap/alignment failed'}
Settings TextBorderSettings;Choose TextBorder '双线框';UI set-value TextPaddingXInput '2'|Out-Null;UI set-value TextPaddingYInput '1'|Out-Null;UI set-value TextBorderTitle '测试'|Out-Null;Done;$framed=Generate
if(!$framed.Document.Text.Contains('测试')-or $framed.Document.Width -ne 36){throw 'Frame title or padding failed'}
Settings TextLibrarySettings;UI wait-for FontTile_1row -t 10000|Out-Null;UI invoke FontTile_1row --action select|Out-Null
UI invoke 'Button_收藏取消收藏'|Out-Null;UI invoke TextFavoritesOnly --action toggle-on|Out-Null
UI wait-for TextGridPage --value '1款' --contains -t 5000|Out-Null
UI screenshot -o (Join-Path $taskData 'favorites.png')|Out-Null;Done
Choose TextMode '系统字体 → 字符画（支持中文）'
Settings TextLayoutSettings;UI invoke TextWrap --action toggle-off|Out-Null;UI set-value TextMaximumWidthInput '0'|Out-Null;Done
UI set-value Field_文字内容 '测试 abc'|Out-Null
Settings TextRasterSettings;Choose TextReadability '中文清晰';UI set-value TextStrokeInput '2'|Out-Null;Choose TextFill '空心';Done
$chinese=Generate;$layout=$chinese.Parameters.layout|ConvertFrom-Json;$raster=$chinese.Parameters.raster|ConvertFrom-Json
if($chinese.Parameters.mode -ne '1'-or $raster.Filled -or $raster.Stroke -ne 2-or $layout.LetterSpacing -ne 2){throw 'Chinese settings not persisted'}
UI screenshot -o (Join-Path $taskData 'chinese-outline.png')|Out-Null
UI set-value Field_文字内容 ([char]0x0378).ToString()|Out-Null;UI invoke TextGenerate|Out-Null
UI wait-for TextStatus --value '请更换字体' --contains -t 5000|Out-Null
UI set-value Field_文字内容 '测试 abc'|Out-Null
UI invoke NavHome|Out-Null;$entry=(Get-Content (Join-Path $taskData 'session.json') -Raw|ConvertFrom-Json).Tabs|Where-Object Path -eq $taskProject|Select-Object -First 1
UI invoke ('ProjectTab_'+$entry.Id.Substring(0,8)) --action select|Out-Null
Settings TextRasterSettings;UI wait-for TextFill --value '空心' -t 3000|Out-Null;Done
UI invoke NavLibrary|Out-Null;UI invoke '打开项目：missing'|Out-Null;UI wait-for '项目字体缺失' -t 5000|Out-Null;UI wait-for ResultEditor --value 'missing' -t 3000|Out-Null
UI invoke TextGenerate|Out-Null;UI wait-for TextStatus --value '请调整设置后重新生成' --contains -t 5000|Out-Null;UI wait-for ResultEditor --value 'missing' -t 3000|Out-Null
if(Test-Path (Join-Path $taskData 'errors.log')){throw 'Text UI recorded application errors'}
& $taskCli ui yield|Out-Null
Write-Output 'PASS FIGlet packing, wrap, border, font favorites/grid, Chinese outline, project restoration and missing font UI'
