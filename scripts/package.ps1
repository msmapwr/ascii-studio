param([ValidateSet('x64','arm64')][string]$Architecture='x64', [string]$PublishDirectory,
    [string]$CertificatePath, [string]$CertificatePassword='password')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
if (!$PublishDirectory) { $PublishDirectory=Join-Path $taskRoot "artifacts/publish-$Architecture" }
$taskLayout=(Resolve-Path -LiteralPath $PublishDirectory).Path
foreach ($taskFile in @('AsciiStudio.exe','AsciiStudio.pri','App.xbf','MainWindow.xbf')) {
    if (!(Test-Path -LiteralPath (Join-Path $taskLayout $taskFile))) { throw "Missing verified publish resource: $taskFile. Run ci-build.ps1 first." }
}
[xml]$taskProject=Get-Content (Join-Path $taskRoot 'src/AsciiStudio/AsciiStudio.csproj') -Raw
$taskVersion=[string]$taskProject.Project.PropertyGroup.Version
$taskOutput=Join-Path $taskRoot 'artifacts/packages'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
[xml]$taskManifest=Get-Content (Join-Path $taskRoot 'packaging/Package.appxmanifest') -Raw
$taskManifest.Package.Identity.ProcessorArchitecture=$Architecture
$taskManifestPath=Join-Path $taskOutput "Package-$Architecture.appxmanifest"
$taskManifest.Save($taskManifestPath)
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskLabel=if ($CertificatePath) { 'signed' } else { 'unsigned' }
$taskArguments=@('package',$taskLayout,'--manifest',$taskManifestPath,'--skip-pri','--output',(Join-Path $taskOutput "AsciiStudio-$taskVersion-win-$Architecture-$taskLabel.msix"))
if($CertificatePath){$taskArguments+=@('--cert',$CertificatePath,'--cert-password',$CertificatePassword)}else{$taskArguments+='--no-sign'}
& winapp @taskArguments
if($LASTEXITCODE -ne 0){throw "MSIX packaging failed: $LASTEXITCODE"}
if (!$CertificatePath) { Write-Output 'Unsigned MSIX: for external signing or Store submission; ordinary sideload installation requires a trusted signature.' }
