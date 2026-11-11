param([Parameter(Mandatory=$true)][string]$EngineRoot)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path ([IO.Path]::GetTempPath()) ('MeddleValidation-' + [Guid]::NewGuid().ToString('N'))
$editor = Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
if (!(Test-Path -LiteralPath $editor)) { throw "UE commandlet executable not found: $editor" }
dotnet run --project (Join-Path $PSScriptRoot 'NativeAssetProbe.csproj') -c Release -- $project
if ($LASTEXITCODE -ne 0) { throw 'Native asset generation failed' }
$log = Join-Path $project 'validation.log'
& $editor (Join-Path $project 'Probe.uproject') -run=pythonscript "-script=$(Join-Path $PSScriptRoot 'validate.py')" -unattended -nullrhi -nosplash -nosound -stdout -FullStdOutLogOutput *> $log
if ($LASTEXITCODE -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'MEDDLE_NATIVE_ASSET_VALIDATION_PASSED' -Quiet)) {
    throw "UE asset validation failed. Log: $log"
}
Write-Output "UE asset validation passed. Assets and log: $project"
