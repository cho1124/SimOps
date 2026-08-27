param([switch]$SkipInstall, [switch]$IncludeUnitySmoke)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $repositoryRoot 'dashboard')
try {
    if (-not $SkipInstall) { npm ci --no-fund; if ($LASTEXITCODE -ne 0) { throw 'Dashboard install failed.' } }
    npm test
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard tests failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard build failed.' }
}
finally { Pop-Location }
& (Join-Path $PSScriptRoot 'Run-Milestone4.ps1') -IncludeRanking -IncludeExperiments -IncludeAnalysis -IncludeUnitySmoke:$IncludeUnitySmoke
Write-Host 'M7 offline regression verified. Real model smoke is separate and uses only an already-installed Ollama model.'
