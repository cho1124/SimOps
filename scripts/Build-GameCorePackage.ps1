param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'SimOps.slnx'
$sourceDirectory = Join-Path $repositoryRoot "src\SimOps.Game.Core\bin\$Configuration\netstandard2.1"
$packageDirectory = Join-Path $repositoryRoot 'unity-client\Packages\com.simops.game-core\Runtime\Plugins'

dotnet build $solutionPath -c $Configuration -m:1 -nodeReuse:false
if ($LASTEXITCODE -ne 0) {
    throw "Game Core build failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'SimOps.Game.Core.dll') -Destination $packageDirectory -Force

$symbolsPath = Join-Path $sourceDirectory 'SimOps.Game.Core.pdb'
if (Test-Path -LiteralPath $symbolsPath) {
    Copy-Item -LiteralPath $symbolsPath -Destination $packageDirectory -Force
}

Write-Host "Game Core package updated: $packageDirectory"
