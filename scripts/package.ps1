param([string]$CertificatePath,[string]$CertificatePassword='password')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot 'artifacts/packages'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
if($LASTEXITCODE -ne 0){throw 'Release build failed.'}
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskLayout=Join-Path $taskRoot 'src/AsciiStudio/bin/Release/net10.0-windows10.0.26100.0/win-x64'
$taskArguments=@('package',$taskLayout,'--manifest',(Join-Path $taskRoot 'packaging/Package.appxmanifest'),'--output',(Join-Path $taskOutput 'AsciiStudio-0.2.0-x64.msix'))
if($CertificatePath){$taskArguments+=@('--cert',$CertificatePath,'--cert-password',$CertificatePassword)}else{$taskArguments+='--no-sign'}
& winapp @taskArguments
if($LASTEXITCODE -ne 0){throw "MSIX packaging failed: $LASTEXITCODE"}
