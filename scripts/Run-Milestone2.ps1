param(
    [ValidateSet('Verify', 'Windows', 'Android', 'All')]
    [string]$Target = 'All'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'unity-client'
$versionFile = Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'
$versionLine = Get-Content -LiteralPath $versionFile | Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
$unityVersion = ($versionLine -split ':', 2)[1].Trim()
$unityEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
$logDirectory = Join-Path $repositoryRoot 'artifacts\unity\logs'

if (-not (Test-Path -LiteralPath $unityEditor)) {
    throw "Unity Editor $unityVersion was not found at $unityEditor."
}

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
& (Join-Path $PSScriptRoot 'Build-GameCorePackage.ps1') -Configuration Release

function Invoke-UnityMethod {
    param(
        [string]$Method,
        [string]$LogName
    )

    $logPath = Join-Path $logDirectory $LogName
    $arguments = @(
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath',
        ('"' + $projectPath + '"'),
        '-executeMethod',
        $Method,
        '-logFile',
        ('"' + $logPath + '"')
    )
    $process = Start-Process -FilePath $unityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Select-String -LiteralPath $logPath -Pattern 'SIMOPS_|error CS|Build Failed|return code' |
        ForEach-Object { Write-Host $_.Line }
    if ($process.ExitCode -ne 0) {
        throw "Unity method $Method failed with exit code $($process.ExitCode). See $logPath."
    }
}

function Invoke-WindowsSmoke {
    $playerPath = Join-Path $repositoryRoot 'artifacts\unity\windows\SimOps.exe'
    $logPath = Join-Path $logDirectory 'player-windows-smoke.log'
    $arguments = @('-batchmode', '-nographics', '--simops-smoke', '-logFile', ('"' + $logPath + '"'))
    $process = Start-Process -FilePath $playerPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Select-String -LiteralPath $logPath -Pattern 'SIMOPS_PLAYER_' | ForEach-Object { Write-Host $_.Line }
    if ($process.ExitCode -ne 0) {
        throw "Windows Player smoke test failed with exit code $($process.ExitCode). See $logPath."
    }
}

if ($Target -in @('Verify', 'All')) {
    Invoke-UnityMethod 'SimOps.Unity.Editor.Milestone2Automation.VerifyGoldenRun' 'verify-golden.log'
}

if ($Target -in @('Windows', 'All')) {
    Invoke-UnityMethod 'SimOps.Unity.Editor.Milestone2Automation.BuildWindowsDevelopment' 'build-windows.log'
    Invoke-WindowsSmoke
}

if ($Target -in @('Android', 'All')) {
    Invoke-UnityMethod 'SimOps.Unity.Editor.Milestone2Automation.BuildAndroidDevelopment' 'build-android.log'
}

Write-Host "Milestone 2 automation completed: $Target"
