param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Run-Milestone2.ps1') -Target WebGL
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'artifacts/unity/web/index.html'))) {
    throw 'Build the WebGL client first.'
}
Write-Host 'Keep Start-LocalLab running for ranked play. This command serves the game and proxies only player/public API routes.'
node (Join-Path $PSScriptRoot 'web-server.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Web host failed.' }
