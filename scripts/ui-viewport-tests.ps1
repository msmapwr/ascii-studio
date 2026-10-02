param([int]$AppPid, [switch]$Prepare)
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$taskData = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/alpha5-ui-data'
$taskProject = Join-Path $taskData 'large.asciiproj'
if ($Prepare) {
    New-Item -ItemType Directory -Force $taskData | Out-Null
    $taskErrorLog = Join-Path $taskData 'errors.log'
    if (Test-Path -LiteralPath $taskErrorLog) { Remove-Item -LiteralPath $taskErrorLog }
    $taskText = ((('x' * 1000) + "`n") * 999) + ('x' * 1000)
    @{Version=3;Document=@{GridVersion=1;Width=1000;Height=1000;Text=$taskText;Title='large'};Mode='text';SourceText='source'} | ConvertTo-Json -Depth 8 | Set-Content $taskProject -Encoding utf8
    Add-Type -AssemblyName System.Drawing
    $taskBitmap = [System.Drawing.Bitmap]::new(64,32)
    $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
    $taskStream = [IO.MemoryStream]::new()
    try {
        $taskGraphics.Clear([System.Drawing.Color]::RoyalBlue)
        $taskGraphics.FillRectangle([System.Drawing.Brushes]::Orange,0,0,32,32)
        $taskBitmap.Save($taskStream,[System.Drawing.Imaging.ImageFormat]::Png)
        $taskImage = [Convert]::ToBase64String($taskStream.ToArray())
    } finally { $taskGraphics.Dispose(); $taskBitmap.Dispose(); $taskStream.Dispose() }
    @{Version=3;Document=@{GridVersion=1;Width=120;Height=30;Text=(((('+#' * 60)+"`n") * 29)+('+#' * 60));Title='compare'};Mode='image';SourceImage=$taskImage;Options=@{Columns=120;CellAspect=.5}} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskData 'compare.asciiproj') -Encoding utf8
    @{RecentFiles=@($taskProject,(Join-Path $taskData 'compare.asciiproj'));AutoConvert=$false} | ConvertTo-Json | Set-Content (Join-Path $taskData 'settings.json') -Encoding utf8
    @{Tabs=@();ActiveId=$null;Recoveries=@()} | ConvertTo-Json | Set-Content (Join-Path $taskData 'session.json') -Encoding utf8
    return
}
if (!$AppPid) { throw 'Provide the isolated test app PID.' }
$taskHwnd = (& winapp ui list-windows -a $AppPid --json | ConvertFrom-Json | Where-Object ownerHwnd -eq 0 | Select-Object -First 1).hwnd
$taskCliPath = (Get-Command winapp).Source
$taskPackage = Get-AppxPackage -Name winapp -ErrorAction SilentlyContinue
if ($taskPackage) { $taskCliPath = Join-Path $taskPackage.InstallLocation 'winapp.exe' }
function UI {
    # Explicit ProcessStartInfo avoids the host's native-output encoding failure.
    Add-Content -LiteralPath (Join-Path $taskData 'ui-commands.log') -Value ($args -join ' ')
    $taskStart = [Diagnostics.ProcessStartInfo]::new($taskCliPath)
    $taskStart.UseShellExecute = $false; $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true; $taskStart.RedirectStandardError = $true
    foreach ($taskArgument in (@('ui') + $args + @('-w', "$taskHwnd"))) { $taskStart.ArgumentList.Add("$taskArgument") }
    try { $taskProcess = [Diagnostics.Process]::Start($taskStart) }
    catch { throw "$($args -join ' '): $($_.Exception.Message)" }
    try {
        $taskOutput = $taskProcess.StandardOutput.ReadToEnd()
        $taskError = $taskProcess.StandardError.ReadToEnd()
        $taskProcess.WaitForExit()
        if ($taskProcess.ExitCode -ne 0) { throw "$($args -join ' '): $taskOutput $taskError" }
        $taskOutput
    } finally { $taskProcess.Dispose() }
}
function Tab([string]$Title) {
    Home
    Start-Sleep -Milliseconds 300
    $taskEntry = (Get-Content (Join-Path $taskData 'session.json') -Raw | ConvertFrom-Json).Tabs | Where-Object Title -eq $Title | Select-Object -First 1
    UI invoke ('ProjectTab_' + $taskEntry.Id.Substring(0,8)) --action select | Out-Null
}
function Home {
    try { UI invoke NavHome | Out-Null }
    catch {
        UI invoke TogglePaneButton | Out-Null
        Start-Sleep -Milliseconds 200
        UI invoke NavHome | Out-Null
    }
}
function Settings([string]$Id) { UI invoke MoreButton | Out-Null; UI invoke $Id | Out-Null }
function Choose([string]$Id,[string]$Value) {
    UI invoke $Id | Out-Null
    $taskChoice = (UI search $Value --json | ConvertFrom-Json).matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Value } | Select-Object -First 1
    if (!$taskChoice) { throw "Missing choice: $Value" }
    UI invoke $taskChoice.selector --action select | Out-Null
}
UI invoke NavLibrary | Out-Null
UI invoke '打开项目：large' | Out-Null
UI wait-for ResultStats --value '可见区域' --contains -t 8000 | Out-Null
Settings ViewportSettings
UI invoke Button_适应窗口 | Out-Null
UI wait-for ResultStats --value '缩小概览' --contains -t 5000 | Out-Null
UI invoke Button_适应宽度 | Out-Null
UI wait-for ResultStats --value '缩小概览' --contains -t 5000 | Out-Null
UI invoke Button_适应窗口 | Out-Null
Tab large
UI screenshot -o (Join-Path $taskData 'large-fit.png') | Out-Null
UI invoke ResultZoomReset | Out-Null
UI wait-for ResultZoomValue --value '100%' -t 3000 | Out-Null
Settings DisplaySettings
UI invoke ResultImagePreview --action toggle-off | Out-Null
Tab large
UI wait-for ResultPageLabel --value '1/' --contains -t 3000 | Out-Null
$taskPage = (UI get-value ResultEditor --json | ConvertFrom-Json).text
if ($taskPage.Length -gt 64100 -or $taskPage.Length -lt 1000) { throw 'Large native editor was not bounded' }
# Windows command lines cannot carry a 64K page. Replace this page with a
# short value through UIA, then verify the entire unseen suffix survives.
UI set-value ResultEditor "EDIT`n" | Out-Null
UI invoke Button_保存项目 | Out-Null
Start-Sleep -Milliseconds 600
$taskSaved = Get-Content $taskProject -Raw | ConvertFrom-Json
if (!$taskSaved.Document.Text.StartsWith("EDIT`n") -or $taskSaved.Document.Text.Length -ne (1000999 - $taskPage.Length + 5) -or !$taskSaved.Document.Text.EndsWith('x' * 1000)) { throw 'Paged save lost unseen content' }
UI invoke ResultUndo | Out-Null
UI invoke Button_保存项目 | Out-Null
Start-Sleep -Milliseconds 400
if ((Get-Content $taskProject -Raw | ConvertFrom-Json).Document.Text.Length -ne 1000999) { throw 'Paged undo lost full content' }
UI invoke Button_下一页 | Out-Null
UI wait-for ResultPageLabel --value '2/' --contains -t 3000 | Out-Null
UI set-value ResultRowJump '900' | Out-Null
UI invoke ResultZoomReset | Out-Null
Settings ViewportSettings
UI invoke Button_定位到选区 | Out-Null
Tab large
UI wait-for ResultStats --value '可见区域' --contains -t 5000 | Out-Null
UI screenshot -o (Join-Path $taskData 'large-selection.png') | Out-Null
UI invoke NavLibrary | Out-Null
UI invoke '打开项目：compare' | Out-Null
Settings ComparisonSettings
Choose ComparisonMode 并排
Tab compare
UI wait-for ComparisonSourceImage -t 5000 | Out-Null
UI wait-for ResultViewportTile -t 5000 | Out-Null
UI screenshot -o (Join-Path $taskData 'compare-side.png') | Out-Null
Settings ComparisonSettings
Choose ComparisonMode 分界线
UI set-value ComparisonSplit '35' | Out-Null
Tab compare
UI wait-for ComparisonDivider -t 3000 | Out-Null
UI screenshot -o (Join-Path $taskData 'compare-wipe.png') | Out-Null
Settings ComparisonSettings
Choose ComparisonSource 原始图片
UI invoke ComparisonSync --action toggle-off | Out-Null
Choose ComparisonMode 原图
Tab compare
UI wait-for ComparisonSourceImage -t 3000 | Out-Null
Settings ComparisonSettings
Choose ComparisonMode 结果
Tab compare
UI invoke ResultZoomIn | Out-Null
UI wait-for ResultZoomValue --value '110%' -t 3000 | Out-Null
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ViewportWindowNative {
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
"@
$taskWindow = (UI inspect --depth 0 --json | ConvertFrom-Json).windows[0]
$taskScale = $taskWindow.scale
$taskDpiContext = [ViewportWindowNative]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
try {
    if (![ViewportWindowNative]::SetWindowPos([IntPtr]$taskHwnd,[IntPtr]::Zero,40,40,[int](480*$taskScale),[int](480*$taskScale),0x14)) { throw 'Narrow window resize failed' }
    Start-Sleep -Milliseconds 350
    Settings ComparisonSettings
    Choose ComparisonMode 并排
    Tab compare
    UI wait-for ComparisonSourceImage -t 3000 | Out-Null
    UI wait-for ResultStats --value '原图预览' --contains -t 3000 | Out-Null
    UI screenshot -o (Join-Path $taskData 'compare-narrow.png') | Out-Null
} finally {
    [ViewportWindowNative]::SetWindowPos([IntPtr]$taskHwnd,[IntPtr]::Zero,40,40,[int](1253*$taskScale),[int](720*$taskScale),0x14) | Out-Null
    [ViewportWindowNative]::SetThreadDpiAwarenessContext($taskDpiContext) | Out-Null
}
if (Test-Path -LiteralPath (Join-Path $taskData 'errors.log')) { throw 'Viewport UI recorded an application error' }
if ((Get-Content (Join-Path $taskData 'settings.json') -Raw | ConvertFrom-Json).PreviewZoom -ne 1.1) { throw 'Manual zoom preference was not saved' }
Write-Output 'PASS viewport fit, paged editing/save/undo, selection, comparison modes and source switch'
