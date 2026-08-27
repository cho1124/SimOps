param([switch]$SkipBuild, [switch]$BackendOnly)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $repositoryRoot 'artifacts\local-lab'
$ownedProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$savedEnvironment = @{}
foreach ($name in @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','SIMOPS_CONNECTION_STRING','SIMOPS_ADMIN_KEY','SIMOPS_TICKET_SIGNING_KEY')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command exited with $LASTEXITCODE" } }
try {
    $ports = @(5080)
    if (-not $BackendOnly) { $ports += 5173 }
    foreach ($port in $ports) {
        $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
        try { $probe.Start() } finally { $probe.Stop() }
    }
    docker compose -f (Join-Path $repositoryRoot 'compose.yaml') -p simops up -d --wait postgres
    Check-Exit
    if (-not $SkipBuild) {
        dotnet restore (Join-Path $repositoryRoot 'SimOps.slnx')
        Check-Exit
        dotnet build (Join-Path $repositoryRoot 'SimOps.slnx') -c Release --no-restore -m:1 -nodeReuse:false
        Check-Exit
    }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:SIMOPS_CONNECTION_STRING = 'Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only;Maximum Pool Size=20'
    if (-not $env:SIMOPS_ADMIN_KEY) { $env:SIMOPS_ADMIN_KEY = 'simops-local-dev-key' }
    if (-not $env:SIMOPS_TICKET_SIGNING_KEY) { $env:SIMOPS_TICKET_SIGNING_KEY = 'simops-local-ticket-signing-key-not-for-production' }
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    foreach ($project in @('SimOps.Api','SimOps.Worker')) {
        $assembly = Join-Path $repositoryRoot "src\$project\bin\Release\net10.0\$project.dll"
        $arguments = '"' + $assembly + '"'
        if ($project -eq 'SimOps.Api') { $arguments += ' --urls http://127.0.0.1:5080' }
        $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logDirectory "$project.stdout.log") -RedirectStandardError (Join-Path $logDirectory "$project.stderr.log")
        $ownedProcesses.Add($process)
    }
    if (-not $BackendOnly) {
        Push-Location (Join-Path $repositoryRoot 'dashboard')
        try {
            if (-not (Test-Path -LiteralPath 'node_modules\vite\bin\vite.js')) { npm ci --no-fund; Check-Exit }
            $vite = Join-Path $repositoryRoot 'dashboard\node_modules\vite\bin\vite.js'
            $process = Start-Process -FilePath 'node' -ArgumentList @(('"' + $vite + '"'),'--host','127.0.0.1','--port','5173','--strictPort') `
                -WorkingDirectory (Join-Path $repositoryRoot 'dashboard') -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $logDirectory 'dashboard.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'dashboard.stderr.log')
            $ownedProcesses.Add($process)
        }
        finally { Pop-Location }
    }
    Write-Host 'SimOps local lab: API http://127.0.0.1:5080 | Dashboard http://127.0.0.1:5173'
    Write-Host 'Keep this terminal running. Ctrl+C stops only the hosts launched here. PostgreSQL is retained.'
    while ($true) {
        foreach ($process in $ownedProcesses) { if ($process.HasExited) { throw "A local lab process exited. Inspect $logDirectory" } }
        Start-Sleep -Seconds 1
    }
}
finally {
    foreach ($process in $ownedProcesses) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
    }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
