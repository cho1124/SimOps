param([switch]$SkipClientBuild)

$ErrorActionPreference = 'Stop'
if (-not $SkipClientBuild) {
    & (Join-Path $PSScriptRoot 'Run-Milestone2.ps1') -Target All
}
& (Join-Path $PSScriptRoot 'Run-Milestone4.ps1') -IncludeRanking -IncludeUnitySmoke
Write-Host 'Milestone 5 automation completed. Android device interaction remains a separate manual check.'
