param([Parameter(Mandatory)][int]$AppPid,[switch]$HomeOnly)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskOutput=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/ui-responsive'
New-Item -ItemType Directory -Force $taskOutput | Out-Null
# Only window geometry/DPI use Win32; every control assertion uses winapp ui.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ResponsiveWindowNative {
 [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
 public static bool Resize(IntPtr hwnd,int x,int y,int width,int height) {
  var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
  try { if(IsZoomed(hwnd)){ShowWindow(hwnd,9);System.Threading.Thread.Sleep(400);} return SetWindowPos(hwnd,IntPtr.Zero,x,y,width,height,0x14); }
  finally { SetThreadDpiAwarenessContext(previous); }
 }
 [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
}
"@
$taskResults=[System.Collections.Generic.List[object]]::new()
function Main-Window {
 for($taskAttempt=0;$taskAttempt -lt 15;$taskAttempt++) {
 $taskWindows=& winapp ui list-windows -a $script:AppPid --json | ConvertFrom-Json
 $script:taskHwnd=($taskWindows | Where-Object {$_.title -like 'Charloom*' -and $_.ownerHwnd -eq 0} | Select-Object -First 1).hwnd
 if($script:taskHwnd){return}
 Start-Sleep -Milliseconds 200
 }
 if(-not $script:taskHwnd){throw 'Main window not found.'}
}
function UI {
 $taskResponse=& winapp ui @args -w $script:taskHwnd 2>&1
 if($LASTEXITCODE -ne 0){throw ($taskResponse -join "`n")}
 $taskResponse
}
function Check([string]$Name,[scriptblock]$Action){
 if($HomeOnly -and $Name -ne 'Narrow home preserves all creation cards'){return}
 try{& $Action | Out-Null;$taskResults.Add([pscustomobject]@{name=$Name;status='PASS'})}
 catch{$taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message})}
}
function Window-Snapshot { (UI inspect --depth 0 --json | ConvertFrom-Json).windows[0] }
function Resize-Window([int]$Width,[int]$Height){
 $taskScale=(Window-Snapshot).scale
 if(-not [ResponsiveWindowNative]::Resize([IntPtr]$taskHwnd,40,40,[int]($Width*$taskScale),[int]($Height*$taskScale))){throw 'SetWindowPos failed.'}
 Start-Sleep -Milliseconds 350
 $taskRect=(Window-Snapshot).elements[0]
 if([Math]::Abs($taskRect.width-$Width*$taskScale)-gt 4 -or [Math]::Abs($taskRect.height-$Height*$taskScale)-gt 4){throw "Requested test size was not applied: expected $($Width*$taskScale) x $($Height*$taskScale), actual $($taskRect.width) x $($taskRect.height)."}
}
function Select-Nav([string]$Nav) {
 try {UI get-property $Nav --json | Out-Null} catch {UI invoke TogglePaneButton | Out-Null;UI wait-for $Nav -t 2000 | Out-Null}
 UI invoke $Nav | Out-Null
}
function Navigate([string]$Nav){Select-Nav NavHome;Select-Nav $Nav;UI wait-for ResultEditor -t 3000 | Out-Null}
function Open-Settings([string]$Id,[string]$Field){
 try{$taskProp=UI get-property $Id --json | ConvertFrom-Json;$taskMore=$taskProp.element.isOffscreen}catch{$taskMore=$true}
 if($taskMore){UI invoke MoreButton | Out-Null}
 UI invoke $Id | Out-Null;UI wait-for $Field -p IsOffscreen --value False -t 2000 | Out-Null
}
Main-Window
Check 'PerMonitorV2 process at actual display DPI' {
 $taskSnapshot=Window-Snapshot
 if($taskSnapshot.dpiAwareness -ne 'per-monitor-aware'){throw 'Process is not per-monitor aware.'}
 if(-not [ResponsiveWindowNative]::AreDpiAwarenessContextsEqual([ResponsiveWindowNative]::GetWindowDpiAwarenessContext([IntPtr]$taskHwnd),[IntPtr](-4))){throw 'Window context is not PerMonitorV2.'}
 $taskSnapshot | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $taskOutput 'dpi.json')
}
Check 'Wide image workspace is side by side' {
 Resize-Window 1200 700;Navigate NavImage;UI wait-for ImageResolution -p IsOffscreen --value False -t 2000
 $taskSource=(UI get-property ImageResolution --json | ConvertFrom-Json).element
 $taskResult=(UI get-property ResultEditor --json | ConvertFrom-Json).element
 if($taskResult.x -le $taskSource.x+$taskSource.width){throw 'Result is not beside input.'}
 UI screenshot -o (Join-Path $taskOutput 'wide.png')
}
Check 'Minimum image workspace preserves editable result' {
 Resize-Window 480 480;UI wait-for WorkspaceInputButton -p IsOffscreen --value False -t 2000
 $taskEditor=(UI get-property ResultEditor --json | ConvertFrom-Json).element
 if($taskEditor.width -lt 500 -or $taskEditor.height -lt 90){throw 'Result canvas is too small or clipped.'}
 UI screenshot -o (Join-Path $taskOutput 'minimum.png')
}
Check 'Minimum input flyout retains source controls' {UI invoke WorkspaceInputButton;UI wait-for ImageResolution -p IsOffscreen --value False -t 2000;UI screenshot -o (Join-Path $taskOutput 'input.png')}
Check 'Narrow resolution flyout stays inside window' {
 Navigate NavImage;Resize-Window 480 600;Open-Settings ImageSizeSettings ImageColumns
 $taskCols=(UI get-property ImageColumns --json | ConvertFrom-Json).element
 $taskRows=(UI get-property ImageRows --json | ConvertFrom-Json).element
 $taskRect=(Window-Snapshot).elements[0]
 if($taskCols.x -lt $taskRect.x -or $taskRows.x+$taskRows.width -gt $taskRect.x+$taskRect.width){throw 'Settings overflow horizontally.'}
 if($taskRows.y -le $taskCols.y){throw 'Settings did not reflow to one column.'}
 UI screenshot -o (Join-Path $taskOutput 'resolution.png')
}
Check 'Text generation works from narrow input flyout' {
 Navigate NavText;UI invoke WorkspaceInputButton;UI wait-for TextGenerate -t 2000;UI wait-for TextFigletFont -p IsOffscreen --value False -t 2000;UI invoke TextGenerate;UI wait-for ResultStats --value '字符' --contains -t 10000
 UI screenshot -o (Join-Path $taskOutput 'text.png')
}
Check 'Generator and tools expose narrow input' {
 Navigate NavGenerator;UI wait-for WorkspaceInputButton -t 2000;UI invoke WorkspaceInputButton;UI wait-for Field_生成器 -p IsOffscreen --value False -t 2000
 Navigate NavTools;UI invoke WorkspaceInputButton;UI wait-for Field_处理方式 -p IsOffscreen --value False -t 2000
}
Check 'Medium workspace keeps result and input access' {Navigate NavImage;Resize-Window 900 600;UI wait-for WorkspaceInputButton -t 2000;UI screenshot -o (Join-Path $taskOutput 'medium.png')}
Check 'Returning wide restores inline inputs without losing result' {Navigate NavText;Resize-Window 1200 700;UI wait-for TextGenerate -p IsOffscreen --value False -t 2000;UI wait-for ResultStats --value '字符' --contains -t 2000}
Check 'Narrow home preserves all creation cards' {
 Resize-Window 480 600;Select-Nav NavHome;UI screenshot -o (Join-Path $taskOutput 'home.png')
 $taskCards=(UI search Button_开始创作 --json | ConvertFrom-Json).matches
 if($taskCards.Count -ne 3){throw 'Expected all three creation buttons.'}
 $taskVisible=@($taskCards | Where-Object { -not $_.isOffscreen })
 if($taskVisible.Count -lt 1){throw 'No creation card is visible.'}
 $taskRect=(Window-Snapshot).elements[0]
 foreach($taskCard in $taskVisible){if($taskCard.width -lt $taskRect.width*.7 -or $taskCard.x+$taskCard.width -gt $taskRect.x+$taskRect.width){throw "Creation card does not fit a single column: $($taskCard | ConvertTo-Json -Compress)"}}
}
Check 'Window placement persists through restart' {
 Resize-Window 900 600;UI invoke Close
 $taskLaunch=& (Join-Path (Split-Path $PSScriptRoot -Parent) 'BuildAndRun.ps1') (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Charloom/Charloom.csproj') -c Release --no-build --detach
 if($LASTEXITCODE -ne 0){throw 'App relaunch failed.'}
 $script:AppPid=[int]($taskLaunch | Select-Object -Last 1);Main-Window
 $taskState=Window-Snapshot;$taskRect=$taskState.elements[0]
 if([Math]::Abs($taskRect.width-900*$taskState.scale)-gt 4 -or [Math]::Abs($taskRect.height-600*$taskState.scale)-gt 4 -or [Math]::Abs($taskRect.x-40)-gt 4 -or [Math]::Abs($taskRect.y-40)-gt 4){throw 'Window size/position did not restore.'}
}
$taskResultFile=if($HomeOnly){'results-home.json'}else{'results.json'}
$taskResults | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskOutput $taskResultFile) -Encoding utf8
$taskResults | Format-Table name,status,detail -AutoSize
Write-Output "Running app PID: $AppPid"
if(@($taskResults | Where-Object status -eq 'FAIL').Count -gt 0){exit 1}
