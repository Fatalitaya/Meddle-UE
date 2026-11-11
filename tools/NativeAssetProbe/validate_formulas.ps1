param([Parameter(Mandatory=$true)][string]$EngineRoot)
$ErrorActionPreference = 'Stop'
python (Join-Path $PSScriptRoot 'make_formula_cases.py')
if ($LASTEXITCODE -ne 0) { throw 'Reference evaluation failed' }
$project = Join-Path ([IO.Path]::GetTempPath()) ('MeddleFormulaValidation-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project (Join-Path $PSScriptRoot 'NativeAssetProbe.csproj') -c Release -- $project (Join-Path $PSScriptRoot 'formula_cases.json')
if ($LASTEXITCODE -ne 0) { throw 'Formula asset generation failed' }
$editor = Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$log = Join-Path $project 'formulas.log'
& $editor (Join-Path $project 'Probe.uproject') -run=pythonscript "-script=$(Join-Path $PSScriptRoot 'validate_formulas.py')" -d3d12 -sm6 -AllowCommandletRendering -RenderOffscreen -unattended -nosplash -nosound -stdout -FullStdOutLogOutput *> $log
if ($LASTEXITCODE -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'MEDDLE_FORMULA_NUMERICS_PASSED' -Quiet)) {
    throw "UE numeric validation failed. Log: $log"
}
$roundtripLog = Join-Path $project 'float-roundtrip.log'
& $editor (Join-Path $project 'Probe.uproject') -run=pythonscript "-script=$(Join-Path $PSScriptRoot 'validate_formulas.py')" -MeddleRoundtripOnly -d3d12 -sm6 -AllowCommandletRendering -RenderOffscreen -unattended -nosplash -nosound -stdout -FullStdOutLogOutput *> $roundtripLog
if ($LASTEXITCODE -ne 0 -or !(Select-String -LiteralPath $roundtripLog -Pattern 'MEDDLE_FORMULA_NUMERICS_PASSED' -Quiet)) {
    throw "UE float texture reload failed. Log: $roundtripLog"
}
Write-Output "UE formula numerics and float texture reload passed. Results: $project"

