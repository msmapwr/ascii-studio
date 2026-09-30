param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskOutput=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/ui-tests'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskResults=[System.Collections.Generic.List[object]]::new()
$taskWindows=& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
$taskMain=@($taskWindows | Where-Object {$_.title -like 'AsciiStudio*' -and $_.ownerHwnd -eq 0})
if($taskMain.Count -ne 1){throw 'Expected one AsciiStudio main window.'}
$taskHwnd=$taskMain[0].hwnd
function Invoke-UI {
    $taskResponse=& winapp ui @args -w $taskHwnd 2>&1
    if($LASTEXITCODE -ne 0){throw ($taskResponse -join "`n")}
    return $taskResponse
}
function Check([string]$Name,[scriptblock]$Action){
    try{& $Action | Out-Null;$taskResults.Add([pscustomobject]@{name=$Name;status='PASS'})}
    catch{$taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message})}
}
Check 'Logo is in header' {Invoke-UI wait-for BrandLogo -t 3000}
Check 'Image navigation and resolution controls' {
    Invoke-UI invoke NavImage;Invoke-UI wait-for ImageColumns -t 3000
    Invoke-UI wait-for ImageRows -p IsEnabled --value False -t 2000
}
Check 'Wheel over columns does not change value' {
    Invoke-UI set-value ImageColumns 240
    Invoke-UI scroll ImageColumns --wheel -2
    Invoke-UI wait-for ImageColumns --value 240 -t 2000
}
Check 'Scroll panel can reach bottom and top' {
    Invoke-UI scroll ParameterScroll --to bottom
    Invoke-UI wait-for ImageReset -p IsOffscreen --value False -t 2000
    Invoke-UI scroll ParameterScroll --to top
    Invoke-UI wait-for ImageResolution -p IsOffscreen --value False -t 2000
}
Check 'Custom rows and large dimensions' {
    Invoke-UI invoke ImageAutoRows
    Invoke-UI wait-for ImageRows -p IsEnabled --value True -t 2000
    Invoke-UI set-value ImageColumns 1920
    Invoke-UI set-value ImageRows 1080
    Invoke-UI wait-for ImageColumns --value 1920 -t 2000
    Invoke-UI wait-for ImageRows --value 1080 -t 2000
}
Check 'Text rendering and result' {
    Invoke-UI invoke NavText;Invoke-UI wait-for TextGenerate -t 3000
    Invoke-UI invoke TextGenerate
    Invoke-UI wait-for ResultStats --value '字符' --contains -t 10000
}
Check 'Export resolution scale changes dimensions' {
    $taskBefore=(Invoke-UI get-value ExportDimensions --json | ConvertFrom-Json).text
    Invoke-UI focus ExportScale
    Invoke-UI send-keys 'down' --via send-input
    $taskAfter=(Invoke-UI get-value ExportDimensions --json | ConvertFrom-Json).text
    if($taskBefore -eq $taskAfter){throw 'Pixel dimensions did not change.'}
}
Check 'Capture result layout' {Invoke-UI screenshot -o (Join-Path $taskOutput 'text-result.png')}
$taskResults | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskOutput 'results.json') -Encoding utf8
$taskResults | Format-Table name,status,detail -AutoSize
if(@($taskResults | Where-Object status -eq 'FAIL').Count -gt 0){exit 1}
