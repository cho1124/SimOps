param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactDirectory = Join-Path $repositoryRoot 'artifacts\experiments\difficulty-curve-001'
$definitionPath = Join-Path $repositoryRoot 'docs\experiments\difficulty-curve-001.json'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Assert-CommandSucceeded {
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" }
}

if (-not $SkipBuild) {
    dotnet restore (Join-Path $repositoryRoot 'SimOps.slnx')
    Assert-CommandSucceeded
    dotnet build (Join-Path $repositoryRoot 'SimOps.slnx') -c Release --no-restore -m:1 -nodeReuse:false
    Assert-CommandSucceeded
}

foreach ($project in @('SimOps.Game.Core.Specs', 'SimOps.Agent.Specs', 'SimOps.Experiment.Specs')) {
    dotnet run --project (Join-Path $repositoryRoot "tests\$project") -c Release --no-build
    Assert-CommandSucceeded
}

$primaryPath = Join-Path $artifactDirectory 'report.json'
$repeatPath = Join-Path $artifactDirectory 'repeat.json'
foreach ($output in @($primaryPath, $repeatPath)) {
    dotnet run --project (Join-Path $repositoryRoot 'src\SimOps.Simulation.Cli') -c Release --no-build -- `
        --experiment $definitionPath --json $output
    Assert-CommandSucceeded
}
$primary = Get-Content -LiteralPath $primaryPath -Raw | ConvertFrom-Json
$repeat = Get-Content -LiteralPath $repeatPath -Raw | ConvertFrom-Json
if ($primary.resultDigest -ne $repeat.resultDigest -or $primary.planHash -ne $repeat.planHash) {
    throw 'Repeated experiment did not produce the same result digest and plan hash.'
}
foreach ($report in @($primary, $repeat)) {
    if ($report.completedRuns -ne 18000 -or $report.replayCheckedRuns -ne 18000 -or
        $report.invalidTransitionCount -ne 0 -or $report.replayMismatchCount -ne 0) {
        throw 'Registered experiment did not complete and replay all 18000 runs without mismatches.'
    }
}
Write-Host "Experiment completed twice with identical result digest: $($primary.resultDigest)"
Write-Host 'No-candidate is a valid result. This command does not publish configs or change seasons.'
