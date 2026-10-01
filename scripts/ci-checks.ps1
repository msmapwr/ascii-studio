$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
[xml]$taskProject = Get-Content (Join-Path $taskRoot 'src/AsciiStudio/AsciiStudio.csproj') -Raw
$taskTag = 'v' + $taskProject.Project.PropertyGroup.Version
$taskCalls = [Collections.Generic.List[string]]::new()
$taskMode = 'new'
# Contract checks use a local CLI substitute: no Release is created by this script.
function gh {
    $taskCalls.Add($args -join ' ')
    $global:LASTEXITCODE = 0
    if ($args[1] -eq 'view') {
        if ($taskMode -eq 'new') { $global:LASTEXITCODE = 1; return }
        return ('{"isDraft":' + $(if ($taskMode -eq 'published') { 'false' } else { 'true' }) + '}')
    }
    if ($args[1] -eq 'upload' -and $taskMode -eq 'fail-upload') { $global:LASTEXITCODE = 1 }
}
& (Join-Path $PSScriptRoot 'ci-release.ps1') -Tag $taskTag
if ($taskCalls.Count -ne 4 -or $taskCalls[1] -notlike '*--draft*' -or $taskCalls[3] -notlike '*--draft=false*') { throw 'Draft publication sequence failed.' }
Write-Output 'PASS draft upload and publication'
$taskCalls.Clear(); $taskMode = 'published'
& (Join-Path $PSScriptRoot 'ci-release.ps1') -Tag $taskTag
if ($taskCalls.Count -ne 1) { throw 'Published release was modified.' }
Write-Output 'PASS rerun preserves published release'
$taskCalls.Clear(); $taskMode = 'fail-upload'
try { & (Join-Path $PSScriptRoot 'ci-release.ps1') -Tag $taskTag; throw 'Upload failure was ignored.' }
catch { if ($_.Exception.Message -ne 'Asset upload failed; release remains a draft.') { throw } }
if ($taskCalls.Count -ne 2) { throw 'Failed upload published the draft.' }
Write-Output 'PASS upload failure retains draft'
$taskChecksumFile = Join-Path $taskRoot 'artifacts/release/SHA256SUMS.txt'
$taskOriginalChecksum = [IO.File]::ReadAllBytes($taskChecksumFile)
try {
    'invalid checksum' | Set-Content $taskChecksumFile
    $taskCalls.Clear()
    try { & (Join-Path $PSScriptRoot 'ci-release.ps1') -Tag $taskTag; throw 'Invalid checksum was accepted.' }
    catch { if ($_.Exception.Message -ne 'Release archive checksum mismatch.') { throw } }
    if ($taskCalls.Count) { throw 'Checksum failure called release API.' }
} finally { [IO.File]::WriteAllBytes($taskChecksumFile, $taskOriginalChecksum) }
Write-Output 'PASS checksum mismatch blocks publication'
$global:LASTEXITCODE = 0
