param([string[]]$Sizes = @('1600x900','1280x720','1560x720'))
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$playerPath = Join-Path $repositoryRoot 'artifacts\unity\windows\SimOps.exe'
$logDirectory = Join-Path $repositoryRoot 'artifacts\unity\logs'
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Build the Windows Player first.' }
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
foreach ($size in $Sizes) {
    if ($size -notmatch '^(\d{3,4})x(\d{3,4})$') { throw "Invalid preview size: $size" }
    $width = [int]$Matches[1]; $height = [int]$Matches[2]
    if ($width -lt 640 -or $height -lt 360 -or $width -gt 2560 -or $height -gt 1440) { throw 'Preview size out of bounds.' }
    $log = Join-Path $logDirectory "ui-preview-$size.log"
    # A rendered Player is needed. No -nographics/-batchmode, no user saves or network calls.
    $arguments = @('--simops-ui-preview','-force-d3d11','-screen-fullscreen','0','-screen-width',$width,'-screen-height',$height,'-logFile',('"' + $log + '"'))
    $process = Start-Process -FilePath $playerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) { throw 'UI preview timed out.' }
        if ($process.ExitCode -ne 0) { throw "UI preview failed. See $log" }
        $content = Get-Content -LiteralPath $log -Raw
        if ($content -notmatch 'SIMOPS_UI_PREVIEW_PASS') { throw "UI preview did not reach its assertions. See $log" }
        if ($content -match 'SIMOPS_UI_FAIL|Exception:|error CS|USS parsing error|was not found in the.*font') { throw "UI runtime reported an error. See $log" }
        Select-String -LiteralPath $log -Pattern 'SIMOPS_UI_' | ForEach-Object { Write-Host $_.Line }
    }
    finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
}
