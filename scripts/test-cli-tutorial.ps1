param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExecutable = Join-Path (Resolve-Path -LiteralPath $PublishDirectory).Path 'charloom-cli.exe'
$taskDirectory = Join-Path $taskRoot "artifacts/tutorial-tests/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $taskDirectory | Out-Null
function Run([string[]]$Arguments) {
    $taskResult = & $taskExecutable --data-directory (Join-Path $taskDirectory 'data') --font-directory (Join-Path $taskDirectory 'fonts') @Arguments
    if ($LASTEXITCODE) { throw "Tutorial command failed: $($Arguments[0..([Math]::Min(1,$Arguments.Length-1))] -join ' ')" }
    $taskResult
}
Push-Location $taskDirectory
try {
    Run @('text','--text','Hello','--output','hello.txt','--save-project','hello.asciiproj') | Out-Null
    Run @('export','--project','hello.asciiproj','--format','PNG','--output','hello.png') | Out-Null
    Run @('text','--text','测试 ASCII','--mode','raster','--system-font','Microsoft YaHei UI','--set','Columns=120','--output','chinese.txt') | Out-Null
    Run @('text','--text','中文','--mode','raster','--set','Columns=160','--set','Bold=true','--set','Filled=false','--set','Stroke=2','--output','outline.txt') | Out-Null
    Run @('text','--text','Hello ASCII Studio','--set','layout.MaximumWidth=100','--set','layout.Wrap=true','--set','layout.Alignment=Center','--set','layout.LetterSpacing=1','--set','layout.Border=1','--set','layout.Title=Demo','--set','layout.PaddingX=2','--output','framed.txt') | Out-Null
    Add-Type -AssemblyName System.Drawing.Common
    $taskBitmap = [Drawing.Bitmap]::new(120,80)
    try { $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap); try { $taskGraphics.Clear([Drawing.Color]::CornflowerBlue) } finally { $taskGraphics.Dispose() }; $taskBitmap.Save((Join-Path $taskDirectory 'photo.png'),[Drawing.Imaging.ImageFormat]::Png) } finally { $taskBitmap.Dispose() }
    Run @('image','--input','photo.png','--columns','120','--output','photo.txt','--save-project','photo.asciiproj') | Out-Null
    Run @('image','--input','photo.png','--set','Style=HalfBlock','--set','Color=true','--format','ANSI','--output','color.ans') | Out-Null
    Run @('image','--input','photo.png','--set','Style=Braille','--set','Color=true','--format','HTML','--output','braille.html') | Out-Null
    Run @('image','--input','photo.png','--set','geometry.Left=10','--set','geometry.Top=10','--set','geometry.Width=80','--set','geometry.Height=80','--set','geometry.QuarterTurns=1','--set','geometry.FlipHorizontal=true','--output','cropped.txt') | Out-Null
    Run @('ansi','--input','color.ans','--encoding','Auto','--format','HTML','--output','ansi.html') | Out-Null
    foreach ($taskKind in 0..6) { Run @('generate','--text','Hello','--set',"Kind=$taskKind",'--output',"pattern-$taskKind.txt") | Out-Null }
    Run @('comment','--project','hello.asciiproj','--syntax','Python','--output','hello-comment.py') | Out-Null
    Run @('comment','--project','hello.asciiproj','--syntax','HTML','--block','--output','hello-comment.html') | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskDirectory 'input.txt'),"Hello`nASCII",[Text.UTF8Encoding]::new($false))
    foreach ($taskAlgorithm in @('UTF-8','UTF-16LE','Base64','GZIP')) {
        $taskOptions = if ($taskAlgorithm -eq 'UTF-16LE') { @('--base64-bytes') } else { @() }
        $taskBom = if ($taskAlgorithm -eq 'UTF-16LE') { @('--bom') } else { @() }
        Run (@('crypto','apply','--algorithm',$taskAlgorithm,'--input','input.txt','--output',"$taskAlgorithm.encoded") + $taskOptions + $taskBom) | Out-Null
        Run (@('crypto','apply','--algorithm',$taskAlgorithm,'--input',"$taskAlgorithm.encoded",'--reverse','--output',"$taskAlgorithm.decoded") + $taskOptions) | Out-Null
        if ([IO.File]::ReadAllText((Join-Path $taskDirectory "$taskAlgorithm.decoded")) -cne "Hello`nASCII") { throw "Tutorial round-trip failed: $taskAlgorithm" }
    }
    Run @('crypto','apply','--algorithm','SHA-256','--input','input.txt','--output','input.sha256.txt') | Out-Null
    New-Item -ItemType Directory -Path 'photos' | Out-Null
    Copy-Item -LiteralPath 'photo.png' -Destination 'photos'
    Run @('batch','image','--input','photos','--output','results','--columns','120','--set','Color=true','--format','PNG') | Out-Null
    Run @('settings','set','--set','Theme=Light','--set','PreviewZoom=1.5','--set','BeginnerMode=true') | Out-Null
    Run @('settings','export','--output','preferences.json') | Out-Null
    Run @('settings','import','--input','preferences.json') | Out-Null
    Write-Output 'PASS tutorial conversion, raster/layout, geometry, ANSI, seven generators, comments, encoding/compression round-trips, batch and isolated settings'
} finally { Pop-Location }
