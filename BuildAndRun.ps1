# Delegate build/run and analyzer injection to the installed Microsoft skill.
$ErrorActionPreference='Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskSkill=Join-Path $env:USERPROFILE '.codex/skills/winui-dev-workflow/BuildAndRun.ps1'
if(!(Test-Path -LiteralPath $taskSkill)){throw 'Install microsoft/win-dev-skills before building.'}
$taskArguments=@($args)
if($taskArguments.Count -eq 0){$taskArguments=@((Join-Path $PSScriptRoot 'src/AsciiStudio/AsciiStudio.csproj'))}
$taskShell=(Get-Process -Id $PID).Path
& $taskShell -NoProfile -File $taskSkill @taskArguments
exit $LASTEXITCODE
