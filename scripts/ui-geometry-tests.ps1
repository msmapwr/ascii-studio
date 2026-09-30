param([int]$AppPid,[switch]$Prepare,[string]$Only)
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot 'artifacts/ui-geometry'
$taskData=Join-Path $taskRoot 'artifacts/geometry-test-data'
New-Item -ItemType Directory -Force $taskOutput,$taskData|Out-Null
if($Prepare){
 Add-Type -AssemblyName System.Drawing
 $taskBitmap=[System.Drawing.Bitmap]::new(80,40);$taskGraphics=[System.Drawing.Graphics]::FromImage($taskBitmap)
 try{
  $taskGraphics.Clear([System.Drawing.Color]::White)
  $taskGraphics.FillRectangle([System.Drawing.Brushes]::Black,0,0,40,40)
  $taskGraphics.FillRectangle([System.Drawing.Brushes]::Red,0,0,20,20)
  $taskGraphics.FillRectangle([System.Drawing.Brushes]::Blue,60,20,20,20)
  $taskBitmap.Save((Join-Path $taskOutput 'source.png'),[System.Drawing.Imaging.ImageFormat]::Png)
 }finally{$taskGraphics.Dispose();$taskBitmap.Dispose()}
 $taskProject=@{Version=1;Document=@{Width=7;Height=1;Text='fixture';Title='geometry-fixture'};Options=@{Columns=120};SourceImage=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $taskOutput 'source.png')));SourceText=$null;Mode='image'}
 $taskFixture=Join-Path $taskOutput 'geometry-fixture.asciiproj'
 $taskProject|ConvertTo-Json -Depth 6|Set-Content $taskFixture -Encoding utf8
 $taskProject|ConvertTo-Json -Depth 6|Set-Content (Join-Path $taskData 'recovery.asciiproj') -Encoding utf8
 @{Theme='Dark';RecentFiles=@($taskFixture)}|ConvertTo-Json|Set-Content (Join-Path $taskData 'settings.json') -Encoding utf8
 return
}
if(-not $AppPid){throw 'Pass -Prepare before launching an isolated test app, then pass -AppPid.'}
$taskHwnd=(& winapp ui list-windows -a $AppPid --json|ConvertFrom-Json|Where-Object ownerHwnd -eq 0|Select-Object -First 1).hwnd
$taskResults=[System.Collections.Generic.List[object]]::new()
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class GeometryTestVisibility {
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 public static void Restore(IntPtr hwnd) {
  if(IsIconic(hwnd)) ShowWindow(hwnd,9);
 }
}
"@
function UI {
 if($args[0] -eq 'invoke' -and $args[1] -like 'Nav*'){
  & winapp ui get-property $args[1] -w $taskHwnd --json 2>&1|Out-Null
  if($LASTEXITCODE -ne 0){& winapp ui invoke TogglePaneButton -w $taskHwnd|Out-Null}
 }
 $taskResponse=& winapp ui @args -w $taskHwnd 2>&1
 if($LASTEXITCODE -ne 0){throw ($taskResponse -join "`n")}
 if($args[0] -eq 'invoke' -and ($args[1] -like 'Nav*' -or $args[1] -eq 'SettingsItem')){Start-Sleep -Milliseconds 250}
 $taskResponse
}
function Check([string]$Name,[scriptblock]$Action){
 if($Only -and $Name -notlike "*$Only*"){return}
 [GeometryTestVisibility]::Restore([IntPtr]$taskHwnd)
 Start-Sleep -Milliseconds 150
 try{& $Action|Out-Null;$taskResults.Add([pscustomobject]@{name=$Name;status='PASS'})}
 catch{$taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message})}
}
function Geometry {
 UI invoke NavText|Out-Null;UI invoke NavImage|Out-Null
 try{$taskMore=(UI get-property ImageGeometrySettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
 if($taskMore){UI invoke MoreButton|Out-Null}
 UI invoke ImageGeometrySettings|Out-Null;UI wait-for CropWidthInput -t 3000|Out-Null
}
function Grid-Size([int]$Width,[int]$Height){UI wait-for ResultStats --value "$Width × $Height" --contains -t 5000|Out-Null}
function Source-Size([int]$Width,[int]$Height){UI wait-for ImageSourceInfo --value "$Width × $Height px" --contains -t 5000|Out-Null}
function Recovery {
 for($taskTry=0;$taskTry -lt 35;$taskTry++){
  $taskSnapshot=Get-Content (Join-Path $taskData 'recovery.asciiproj') -Raw|ConvertFrom-Json
  if($taskSnapshot.Geometry.QuarterTurns -eq 1 -and $taskSnapshot.Geometry.Width -eq 50 -and $taskSnapshot.Geometry.FlipHorizontal){return $taskSnapshot}
  Start-Sleep -Milliseconds 100
 }
 throw 'Updated geometry was not saved in recovery project.'
}
Start-Sleep -Milliseconds 1200
Check 'Legacy image project remains editable' {
 UI invoke NavLibrary;UI wait-for '打开项目：geometry-fixture' -t 3000
 UI invoke '打开项目：geometry-fixture';UI wait-for ImageSourceInfo --value '80 × 40 px' --contains -t 5000
 UI invoke Button_转换;Grid-Size 120 30;Geometry
 UI wait-for ImageRotateRight -p IsEnabled --value True -t 2000
 UI wait-for ImageGeometryUndo -p IsEnabled --value False -t 2000
}
Check '90 degree rotation updates preview and automatic output' {
 Geometry
 UI invoke ImageRotateRight;UI wait-for ImageRotationValue --value '旋转 90°' -t 2000
 Source-Size 40 80;Grid-Size 120 120
 UI screenshot -o (Join-Path $taskOutput 'rotated.png')
}
Check 'Undo redo and half turn are consistent' {
 Geometry
 UI invoke ImageGeometryUndo;Source-Size 80 40;Grid-Size 120 30
 UI invoke ImageGeometryRedo;Source-Size 40 80;Grid-Size 120 120
 UI invoke ImageRotateLeft;Source-Size 80 40;Grid-Size 120 30
 UI invoke ImageRotateHalf;UI wait-for ImageRotationValue --value '旋转 180°' -t 2000;Grid-Size 120 30
}
Check 'Percent crop and flip preserve a reversible original' {
 Geometry
 UI invoke ImageGeometryReset;Grid-Size 120 30
 UI set-value CropWidthInput 50;UI invoke ImageCropApply
 Source-Size 40 40;Grid-Size 120 60
 $taskBefore=(UI get-value ResultEditor --json|ConvertFrom-Json).text
 UI invoke ImageFlipHorizontal --action toggle-on
 Start-Sleep -Milliseconds 700
 $taskAfter=(UI get-value ResultEditor --json|ConvertFrom-Json).text
 if($taskBefore -eq $taskAfter){throw 'Flip did not change generated content.'}
 UI invoke ImageFlipVertical --action toggle-on;Grid-Size 120 60
 UI invoke ImageGeometryUndo;UI wait-for ImageFlipVertical --value Off -t 2000
 UI invoke ImageRotateRight;Source-Size 40 40;Grid-Size 120 60
 UI screenshot -o (Join-Path $taskOutput 'crop.png')
}
Check 'Invalid crop keeps the last successful result' {
 Geometry
 $taskBefore=(UI get-value ResultEditor --json|ConvertFrom-Json).text
 UI set-value CropLeftInput 80;UI invoke ImageCropApply
 UI wait-for '裁剪区域必须在原图范围内' --contains -t 3000
 if((UI get-value ResultEditor --json|ConvertFrom-Json).text -ne $taskBefore){throw 'Invalid crop replaced the result.'}
 UI invoke ImageGeometryUndo;UI invoke ImageGeometryRedo
}
Check 'Project recovery retains original image and geometry' {
 $taskSaved=Recovery
 if($taskSaved.SourceImage -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $taskOutput 'source.png')))){throw 'Project overwrote its original image.'}
 UI invoke NavHome;UI invoke Button_恢复最近一次结果;Source-Size 40 40;Geometry
 UI wait-for CropWidthInput --value 50 -t 2000;UI wait-for ImageRotationValue --value '旋转 90°' -t 2000
 UI wait-for ImageFlipHorizontal --value On -t 2000
 UI invoke ImageGeometryReset;Source-Size 80 40;Grid-Size 120 30
 UI invoke ImageGeometryUndo;Source-Size 40 40;Grid-Size 120 60
}
Check 'Manual conversion preference preserves output until requested' {
 UI invoke SettingsItem;UI wait-for SettingsAutoConvert -t 3000;UI invoke SettingsAutoConvert --action toggle-off
 Geometry;$taskBefore=(UI get-value ResultEditor --json|ConvertFrom-Json).text
 UI invoke ImageGeometryReset;Source-Size 80 40;Start-Sleep -Milliseconds 500
 if((UI get-value ResultEditor --json|ConvertFrom-Json).text -ne $taskBefore){throw 'Manual mode automatically replaced output.'}
 UI invoke NavText;UI invoke NavImage;UI invoke Button_转换;Grid-Size 120 30
 UI invoke SettingsItem;UI wait-for SettingsAutoConvert -t 3000;UI invoke SettingsAutoConvert --action toggle-on
}
Check 'Generated color metadata survives delayed editor notifications' {
 UI invoke NavImage
 try{$taskMore=(UI get-property ImageCharacterSettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
 if($taskMore){UI invoke MoreButton}
 UI invoke ImageCharacterSettings;UI wait-for Field_字符集由浅到深 -t 3000
 UI set-value Field_字符集由浅到深 ' .:-=+*#%@'
 UI invoke NavText;UI invoke NavImage
 try{$taskMore=(UI get-property ImageAdjustmentSettings --json|ConvertFrom-Json).element.isOffscreen}catch{$taskMore=$true}
 if($taskMore){UI invoke MoreButton}
 UI invoke ImageAdjustmentSettings;UI wait-for '保留原图颜色' -t 3000
 UI invoke '保留原图颜色' --action toggle-on
 UI wait-for ResultStats --value ' ms' --contains -t 5000
 $taskColored=$false
 for($taskTry=0;$taskTry -lt 35;$taskTry++){
  $taskSnapshot=Get-Content (Join-Path $taskData 'recovery.asciiproj') -Raw|ConvertFrom-Json
  if($taskSnapshot.Document.Colors.Count -eq 3600){$taskColored=$true;break}
  Start-Sleep -Milliseconds 100
 }
 if(-not $taskColored){throw 'Generated color grid was lost before saving.'}
}
Check 'Crop fields reflow inside a narrow window' {
 Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class GeometryTestWindow {
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
 public static void Resize(IntPtr hwnd,int w,int h) {
  var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
  try { if(!SetWindowPos(hwnd,IntPtr.Zero,20,20,w,h,0x14)) throw new Exception("Resize failed"); }
  finally { SetThreadDpiAwarenessContext(previous); }
 }
}
"@
 $taskScale=(UI inspect --depth 0 --json|ConvertFrom-Json).windows[0].scale
 try{
  [GeometryTestWindow]::Resize([IntPtr]$taskHwnd,[int](480*$taskScale),[int](600*$taskScale));Start-Sleep -Milliseconds 350
  Geometry
  $taskLeft=(UI get-property CropLeftInput --json|ConvertFrom-Json).element
  $taskTop=(UI get-property CropTopInput --json|ConvertFrom-Json).element
  $taskBounds=(UI inspect --depth 0 --json|ConvertFrom-Json).windows[0].elements[0]
  $taskViewport=(UI get-property AdaptiveSettingsScroll --json|ConvertFrom-Json).element
  if($taskTop.y -le $taskLeft.y){throw 'Crop fields did not reflow into one column.'}
  if($taskLeft.x -lt $taskBounds.x -or $taskLeft.x+$taskLeft.width -gt $taskBounds.x+$taskBounds.width){throw 'Crop field extends beyond window.'}
  if($taskViewport.y+$taskViewport.height -gt $taskBounds.y+$taskBounds.height){throw 'Settings scroll viewport is clipped below the window.'}
  UI screenshot -o (Join-Path $taskOutput 'narrow-crop.png')
  UI scroll AdaptiveSettingsScroll --to bottom
  UI wait-for ImageGeometryReset -p IsOffscreen --value False -t 2000
  UI screenshot -o (Join-Path $taskOutput 'narrow-actions.png')
 }finally{[GeometryTestWindow]::Resize([IntPtr]$taskHwnd,1880,1088)}
}
$taskResultFile=if($Only){'results-targeted.json'}else{'results.json'}
$taskResults|ConvertTo-Json -Depth 5|Set-Content (Join-Path $taskOutput $taskResultFile) -Encoding utf8
$taskResults|Format-Table name,status,detail -AutoSize
if(@($taskResults|Where-Object status -eq 'FAIL').Count -gt 0){exit 1}
