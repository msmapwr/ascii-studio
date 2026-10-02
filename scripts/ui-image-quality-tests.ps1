param([int]$AppPid, [switch]$Prepare, [ValidateSet('all','cancel','quality')][string]$Phase = 'all')
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$taskData = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/alpha6-ui-data'
$taskProject = Join-Path $taskData 'quality.asciiproj'
if ($Prepare) {
    New-Item -ItemType Directory -Force $taskData | Out-Null
    $taskErrorLog = Join-Path $taskData 'errors.log'
    if (Test-Path -LiteralPath $taskErrorLog) { Remove-Item -LiteralPath $taskErrorLog }
    Add-Type -AssemblyName System.Drawing
    $taskBitmap = [Drawing.Bitmap]::new(64,48)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    $taskStream = [IO.MemoryStream]::new()
    try {
        $taskGraphics.Clear([Drawing.Color]::Transparent)
        $taskGraphics.FillRectangle([Drawing.Brushes]::Black,8,8,24,32)
        $taskGraphics.FillRectangle([Drawing.Brushes]::Orange,32,8,24,32)
        $taskGraphics.FillEllipse([Drawing.Brushes]::RoyalBlue,12,12,32,24)
        $taskBitmap.Save($taskStream,[Drawing.Imaging.ImageFormat]::Png)
        $taskImage = [Convert]::ToBase64String($taskStream.ToArray())
    } finally { $taskGraphics.Dispose(); $taskBitmap.Dispose(); $taskStream.Dispose() }
    @{Version=3;Document=@{GridVersion=1;Width=7;Height=1;Text='fixture';Title='quality'};Mode='image';SourceImage=$taskImage;Options=@{Columns=120;CellAspect=.5}} | ConvertTo-Json -Depth 8 | Set-Content $taskProject -Encoding utf8
    @{RecentFiles=@($taskProject);AutoConvert=$false;Animations=$false} | ConvertTo-Json | Set-Content (Join-Path $taskData 'settings.json') -Encoding utf8
    @{Tabs=@();ActiveId=$null;Recoveries=@()} | ConvertTo-Json | Set-Content (Join-Path $taskData 'session.json') -Encoding utf8
    return
}
if (!$AppPid) { throw 'Provide isolated test app PID.' }
$env:WINAPP_UI_WORKFLOW_ID = [Guid]::NewGuid().ToString()
$taskHwnd = (& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json | Where-Object ownerHwnd -eq 0 | Select-Object -First 1).hwnd
$taskCliPath = (Get-Command winapp).Source
$taskPackage = Get-AppxPackage -Name winapp -ErrorAction SilentlyContinue
if ($taskPackage) { $taskCliPath = Join-Path $taskPackage.InstallLocation 'winapp.exe' }
function UI {
    Add-Content -LiteralPath (Join-Path $taskData 'ui-commands.log') -Value ($args -join ' ')
    $taskStart = [Diagnostics.ProcessStartInfo]::new($taskCliPath)
    $taskStart.UseShellExecute = $false; $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true; $taskStart.RedirectStandardError = $true
    foreach ($taskArgument in (@('ui') + $args + @('-w', "$taskHwnd"))) { $taskStart.ArgumentList.Add("$taskArgument") }
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    try {
        $taskOutput = $taskProcess.StandardOutput.ReadToEnd(); $taskError = $taskProcess.StandardError.ReadToEnd()
        $taskProcess.WaitForExit()
        if ($taskProcess.ExitCode -ne 0) { throw "$($args -join ' '): $taskOutput $taskError" }
        $taskOutput
    } finally { $taskProcess.Dispose() }
}
function Tab {
    UI invoke NavHome | Out-Null
    $taskEntry = (Get-Content (Join-Path $taskData 'session.json') -Raw | ConvertFrom-Json).Tabs | Where-Object Title -eq quality | Select-Object -First 1
    UI invoke ('ProjectTab_' + $taskEntry.Id.Substring(0,8)) --action select | Out-Null
}
function Settings([string]$Id) { UI invoke MoreButton | Out-Null; UI invoke $Id | Out-Null }
function Choose([string]$Id,[string]$Value) {
    UI invoke $Id | Out-Null
    $taskChoice = (UI search $Value --json | ConvertFrom-Json).matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Value } | Select-Object -First 1
    if (!$taskChoice) { throw "Missing choice: $Value" }
    UI invoke $taskChoice.selector --action select | Out-Null
}
function Generate {
    Tab; UI invoke Button_转换 | Out-Null
    UI wait-for ImageProcessingStatus --value '字符' --contains -t 15000 | Out-Null
    UI invoke Button_保存项目 | Out-Null
    Start-Sleep -Milliseconds 400
    Get-Content $taskProject -Raw | ConvertFrom-Json
}
UI invoke NavLibrary | Out-Null; UI invoke '打开项目：quality' | Out-Null
if ($Phase -eq 'cancel') {
    Settings ImageCharacterSettings; UI invoke ImageMeasuredDensity --action toggle-on | Out-Null
    Tab; Settings ImageSizeSettings
    UI invoke ImageAutoRows --action toggle-off | Out-Null
    UI set-value ImageColumnsInput '2000' | Out-Null; UI set-value ImageRowsInput '2000' | Out-Null
    Tab; UI send-keys 'enter esc' --target Button_转换 --via send-input | Out-Null
    UI wait-for ImageProcessingStatus --value '已取消' --contains -t 5000 | Out-Null
    UI invoke Button_保存项目 | Out-Null; Start-Sleep -Milliseconds 400
    if ((Get-Content $taskProject -Raw | ConvertFrom-Json).Document.Text -ne 'fixture') { throw 'Cancelled task replaced the old result' }
    UI screenshot -o (Join-Path $taskData 'cancel.png') | Out-Null
    & $taskCliPath ui yield | Out-Null
    Write-Output 'PASS active conversion cancellation'
    return
}
Settings ImageCharacterSettings
if ((UI get-property ImageMeasuredDensity --json | ConvertFrom-Json).element.toggleState -ne 'off') { throw 'Old project unexpectedly enabled measured mapping' }
UI invoke ImageMeasuredDensity --action toggle-on | Out-Null
$taskSaved = Generate
if (!$taskSaved.Options.MeasureGlyphDensity -or $taskSaved.Version -ne 4 -or (Get-Content ($taskProject+'.bak') -Raw | ConvertFrom-Json).Version -ne 3) { throw 'Measured mapping or v3 backup lost' }
foreach ($taskStyle in @(@('结构线条',1),@('Braille 点阵',2),@('半块双色',3))) {
    Choose ImageArtStyle $taskStyle[0]
    $taskSaved = Generate
    if ($taskSaved.Options.Style -ne $taskStyle[1] -or $taskSaved.Document.Width -ne 120) { throw "Style not saved: $($taskStyle[0])" }
    if ($taskStyle[1] -eq 2 -and !$taskSaved.Document.Text.Contains('⣿') -and !($taskSaved.Document.Text.ToCharArray() | Where-Object { [int]$_ -ge 0x2801 -and [int]$_ -le 0x28ff })) { throw 'Braille output missing' }
    if ($taskStyle[1] -eq 3 -and !$taskSaved.Document.BackgroundColors) { throw 'Half-block background grid missing' }
}
Settings ImagePaletteSettings
UI invoke ImagePreserveAlpha --action toggle-on | Out-Null
UI invoke ImageTrimAlpha --action toggle-on | Out-Null
Choose ImagePalette 'ANSI 256 色'
$taskSaved = Generate
if (!$taskSaved.Options.PreserveTransparent -or !$taskSaved.Options.TrimTransparent -or $taskSaved.Document.ColorEncoding -ne 2) { throw 'Transparency/palette not saved' }
UI wait-for ImageSourceInfo --value '48 × 32' --contains -t 5000 | Out-Null
Settings ImagePaletteSettings
Choose ImagePalette '自定义颜色'
UI set-value ImagePaletteColors '#00FF00,#000000' | Out-Null
$taskSaved = Generate
if ($taskSaved.Document.Colors | Where-Object { ($_ -band 0xFFFFFF) -notin @(0,65280) }) { throw 'Custom palette contains unexpected color' }
Settings ImageAdjustmentSettings; UI set-value ImageAdaptive '0.8' | Out-Null
$taskSaved = Generate
if ([Math]::Abs($taskSaved.Options.AdaptiveStrength - .8) -gt .01) { throw 'Adaptive strength not saved' }
UI invoke Button_转换 | Out-Null
UI wait-for ImageProcessingStatus --value '字符' --contains -t 10000 | Out-Null
Settings ImagePerformanceSettings
UI wait-for ImageCacheStatus --value '累计复用' --contains -t 3000 | Out-Null
UI invoke ImageClearCache | Out-Null
UI wait-for ImageCacheStatus --value '已清空' --contains -t 3000 | Out-Null
Tab
Settings DisplaySettings; UI invoke ResultImagePreview --action toggle-on | Out-Null
Tab; UI wait-for ResultViewportTile -t 5000 | Out-Null
UI screenshot -o (Join-Path $taskData 'halfblock-palette.png') | Out-Null
Settings DisplaySettings; UI invoke ResultImagePreview --action toggle-off | Out-Null
Tab; Choose ImageArtStyle '密度字符'
if ($Phase -eq 'all') {
Settings ImageSizeSettings
UI invoke ImageAutoRows --action toggle-off | Out-Null
UI set-value ImageColumnsInput '2000' | Out-Null; UI set-value ImageRowsInput '2000' | Out-Null
Tab
# Start and cancel within one input workflow; a second CLI command can arrive
# after a fast conversion has already finished.
UI send-keys 'enter esc' --target Button_转换 --via send-input | Out-Null
UI wait-for ImageProcessingStatus --value '已取消' --contains -t 5000 | Out-Null
UI invoke Button_保存项目 | Out-Null; Start-Sleep -Milliseconds 400
if ((Get-Content $taskProject -Raw | ConvertFrom-Json).Document.Text -ne $taskSaved.Document.Text) { throw 'Cancellation replaced output' }
}
Settings ImageSizeSettings
UI invoke ImageAutoRows --action toggle-on | Out-Null; UI set-value ImageColumnsInput '240' | Out-Null
Tab; UI invoke SettingsItem | Out-Null; UI invoke SettingsAutoConvert --action toggle-on | Out-Null
Tab; Settings ImageSizeSettings; UI set-value ImageColumnsInput '960' | Out-Null
UI invoke SettingsDone | Out-Null
UI wait-for ImageProcessingStatus --value '960 ×' --contains -t 15000 | Out-Null
UI invoke Button_保存项目 | Out-Null; Start-Sleep -Milliseconds 500
$taskFull = Get-Content $taskProject -Raw | ConvertFrom-Json
if ($taskFull.Document.Width -ne 960 -or !$taskFull.Options.QuickPreview) { throw 'Automatic preview was saved instead of the full document' }
# Large documents default to the paged image view. Check edit protection on
# a complete small document after independently verifying the full 960-column save.
Settings ImageSizeSettings; UI set-value ImageColumnsInput '120' | Out-Null
UI invoke SettingsDone | Out-Null
UI wait-for ImageProcessingStatus --value '120 ×' --contains -t 15000 | Out-Null
Settings DisplaySettings; UI invoke ResultImagePreview --action toggle-off | Out-Null
UI invoke SettingsDone | Out-Null
UI set-value ResultEditor 'HAND EDIT' | Out-Null
$taskProtected = Generate
if ($taskProtected.Document.Text -ne 'HAND EDIT' -or !$taskProtected.Edited) { throw 'Image conversion overwrote a manual edit' }
UI wait-for ResultProtection -t 3000 | Out-Null
if (Test-Path -LiteralPath (Join-Path $taskData 'errors.log')) { throw 'Image-quality UI recorded an application error' }
Write-Output "PASS image-quality phase $Phase (keyboard cancellation is separately required for phase quality)"
& $taskCliPath ui yield | Out-Null
