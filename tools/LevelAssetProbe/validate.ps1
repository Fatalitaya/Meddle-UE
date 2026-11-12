param([Parameter(Mandatory=$true)][string]$EngineRoot)
$ErrorActionPreference = 'Stop'
$project = Join-Path ([IO.Path]::GetTempPath()) ('MeddleLevelValidation-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project (Join-Path $PSScriptRoot 'LevelAssetProbe.csproj') -c Release -- $project
if ($LASTEXITCODE -ne 0) { throw 'Level generation failed' }
$editor = Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
foreach ($pass in @('initial', 'reload', 'sublevel')) {
    $scriptName = if ($pass -eq 'sublevel') { 'validate_sublevel.py' } else { 'validate.py' }
    $marker = if ($pass -eq 'sublevel') { 'MEDDLE_SUBLEVEL_VALIDATION_PASSED' } else { 'MEDDLE_LEVEL_VALIDATION_PASSED' }
    $log = Join-Path $project ($pass + '.log')
    & $editor (Join-Path $project 'Probe.uproject') -run=pythonscript "-script=$(Join-Path $PSScriptRoot $scriptName)" -unattended -nullrhi -nosplash -nosound -stdout -FullStdOutLogOutput *> $log
    if ($LASTEXITCODE -ne 0 -or !(Select-String -LiteralPath $log -Pattern $marker -Quiet)) {
        throw "Level validation failed: $log"
    }
}
Write-Output "Level creation, reload and sublevel validation passed: $project"
