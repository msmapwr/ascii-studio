param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskOutput=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/ui-layout'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskResults=[System.Collections.Generic.List[object]]::new()
$taskWindows=& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
$taskHwnd=($taskWindows | Where-Object {$_.title -like 'Charloom*' -and $_.ownerHwnd -eq 0} | Select-Object -First 1).hwnd
if(-not $taskHwnd){throw 'Charloom main window not found.'}
function Invoke-UI {
    $taskResponse=& winapp ui @args -w $taskHwnd 2>&1
    if($LASTEXITCODE -ne 0){throw ($taskResponse -join "`n")}
    return $taskResponse
}
function Check([string]$Name,[scriptblock]$Action){
    try{& $Action | Out-Null;$taskResults.Add([pscustomobject]@{name=$Name;status='PASS'})}
    catch{$taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message})}
}
function No-ParameterScroll {
    $taskProps=Invoke-UI get-property ParameterScroll --json | ConvertFrom-Json
    if($taskProps.properties.ScrollVerticalPercent -ne '-1'){throw 'Parameter pane still needs vertical scrolling.'}
}
function Open-Settings([string]$Button,[string]$Field){
    try {$taskProps=Invoke-UI get-property $Button --json | ConvertFrom-Json; $taskOverflow=$taskProps.element.isOffscreen} catch {$taskOverflow=$true}
    if($taskOverflow){Invoke-UI invoke MoreButton | Out-Null}
    Invoke-UI invoke $Button | Out-Null
    Invoke-UI wait-for $Field -p IsOffscreen --value False -t 2000 | Out-Null
}
Check 'Image pane requires no scrolling' {Invoke-UI invoke NavImage;Invoke-UI wait-for ImageResolution -t 3000;No-ParameterScroll;Invoke-UI screenshot -o (Join-Path $taskOutput 'image.png')}
Check 'Custom resolution is accessible' {Open-Settings ImageSizeSettings ImageColumns;Invoke-UI wait-for ImageRows -t 2000;Invoke-UI screenshot -o (Join-Path $taskOutput 'resolution.png');Invoke-UI send-keys escape --via send-input}
Check 'Character settings are accessible' {Open-Settings ImageCharacterSettings Field_抖动算法;Invoke-UI send-keys escape --via send-input}
Check 'Picture adjustments are accessible' {Open-Settings ImageAdjustmentSettings ImageBrightness;Invoke-UI wait-for Field_锐化 -p IsOffscreen --value False -t 2000;Invoke-UI screenshot -o (Join-Path $taskOutput 'adjustments.png');Invoke-UI send-keys escape --via send-input}
Check 'Overflow effects are accessible' {Open-Settings ImageEffectSettings ImageReset;Invoke-UI send-keys escape --via send-input}
Check 'Text pane requires no scrolling and generates' {Invoke-UI invoke NavText;Invoke-UI wait-for TextGenerate -t 3000;No-ParameterScroll;Invoke-UI invoke TextGenerate;Invoke-UI wait-for ResultStats --value '字符' --contains -t 10000}
Check 'Primary text font is accessible' {Invoke-UI wait-for TextFigletFont -p IsOffscreen --value False -t 2000}
Check 'Text layout is accessible' {Open-Settings TextLayoutSettings Field_边框;Invoke-UI send-keys escape --via send-input}
Check 'Export settings are accessible' {Open-Settings ExportSettings ExportScale;Invoke-UI wait-for Field_导出格式 -t 2000;Invoke-UI send-keys escape --via send-input}
Check 'Display settings are accessible' {Open-Settings DisplaySettings ResultImagePreview;Invoke-UI send-keys escape --via send-input}
Check 'Generator pane requires no scrolling' {Invoke-UI invoke '生成器';Invoke-UI wait-for GeneratorSettings -t 3000;No-ParameterScroll;Open-Settings GeneratorSettings Field_随机种子;Invoke-UI screenshot -o (Join-Path $taskOutput 'generator.png');Invoke-UI send-keys escape --via send-input}
$taskResults | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskOutput 'results.json') -Encoding utf8
$taskResults | Format-Table name,status,detail -AutoSize
if(@($taskResults | Where-Object status -eq 'FAIL').Count -gt 0){exit 1}
