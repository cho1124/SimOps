param([switch]$SkipInstall, [switch]$SkipUnity)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Run-Milestone7.ps1') -SkipInstall:$SkipInstall
if (-not $SkipUnity) { & (Join-Path $PSScriptRoot 'Run-Milestone2.ps1') -Target All }
$mode = '--liveops'
if (-not $SkipUnity) { $mode = '--liveops-unity' }
Push-Location $repositoryRoot
try {
    dotnet run --project tests/SimOps.Backend.Specs -c Release --no-build -- $mode
    if ($LASTEXITCODE -ne 0) { throw 'Isolated LiveOps verification failed.' }
}
finally { Pop-Location }
Write-Host 'M8 verified in an isolated database. The real active season and human decisions are unchanged.'
