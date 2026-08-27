param([switch]$IncludeUnitySmoke, [switch]$SkipInstall)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $repositoryRoot 'dashboard')
try {
    if (-not $SkipInstall) {
        npm ci --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Dashboard dependency installation failed.' }
    }
    npm test
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard tests failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard build failed.' }
}
finally { Pop-Location }
& (Join-Path $PSScriptRoot 'Run-Milestone4.ps1') -IncludeRanking -IncludeExperiments -IncludeUnitySmoke:$IncludeUnitySmoke
Write-Host 'Milestone 6 verified. Registered results are persisted without a human decision or publication.'
