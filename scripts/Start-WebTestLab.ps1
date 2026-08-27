param([ValidateRange(1,60)][int]$Minutes = 30)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$databaseName = 'simops_web_spec_' + [Guid]::NewGuid().ToString('N')
$logDirectory = Join-Path $repositoryRoot 'artifacts/web-test'
$stopFile = Join-Path $logDirectory ($databaseName + '.stop')
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$saved = @{}
foreach ($key in @('SIMOPS_CONNECTION_STRING','ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','SIMOPS_ADMIN_KEY','SIMOPS_APPROVER_KEY','SIMOPS_TICKET_SIGNING_KEY','SIMOPS_ANALYSIS_PROVIDER')) { $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
$created = $false
try {
    foreach ($relative in @('src/SimOps.Api/bin/Release/net10.0/SimOps.Api.dll','src/SimOps.Worker/bin/Release/net10.0/SimOps.Worker.dll','artifacts/unity/web/index.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relative))) { throw "Build the backend and Web client first. Missing: $relative" }
    }
    foreach ($port in @(5081,5175)) {
        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
        try { $probe.Start() } finally { $probe.Stop() }
    }
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    docker exec simops-postgres psql -U simops -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $databaseName"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated database.' }; $created = $true
    $env:SIMOPS_CONNECTION_STRING = "Host=127.0.0.1;Port=54329;Database=$databaseName;Username=simops;Password=simops-local-only;Maximum Pool Size=20"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'
    $env:SIMOPS_ADMIN_KEY = 'web-fixture-admin'; $env:SIMOPS_APPROVER_KEY = 'web-fixture-approver'
    $env:SIMOPS_TICKET_SIGNING_KEY = 'web-fixture-signing-key-not-production'
    $env:SIMOPS_ANALYSIS_PROVIDER = 'offline'
    foreach ($project in @('SimOps.Api','SimOps.Worker')) {
        $assembly = Join-Path $repositoryRoot "src/$project/bin/Release/net10.0/$project.dll"
        $arguments = '"' + $assembly + '"'
        if ($project -eq 'SimOps.Api') { $arguments += ' --urls http://127.0.0.1:5081' }
        $process = Start-Process -FilePath dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory "$databaseName-$project.log") -RedirectStandardError (Join-Path $logDirectory "$databaseName-$project.stderr.log")
        $processes.Add($process)
    }
    $server = Join-Path $PSScriptRoot 'web-server.mjs'
    $processes.Add((Start-Process -FilePath node -ArgumentList @(('"' + $server + '"'),'--port','5175','--api-url','http://127.0.0.1:5081','--mount',"/$databaseName/") -WindowStyle Hidden -PassThru))
    Write-Host "Web test: http://127.0.0.1:5175/$databaseName/ | isolated DB: $databaseName"
    Write-Host "Stops after $Minutes minutes or when this exact file is created: $stopFile"
    $deadline = [DateTime]::UtcNow.AddMinutes($Minutes)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $stopFile)) {
        foreach ($process in $processes) { if ($process.HasExited) { throw "A test host exited: $($process.Id)" } }
        Start-Sleep -Seconds 1
    }
} finally {
    foreach ($process in $processes) { if (-not $process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }; $process.Dispose() }
    if ($created) {
        if ($databaseName -notmatch '^simops_web_spec_[a-f0-9]{32}$') { throw 'Invalid test database cleanup target.' }
        docker exec simops-postgres psql -U simops -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE $databaseName WITH (FORCE)"
        if ($LASTEXITCODE -ne 0) { Write-Warning "Test DB cleanup failed: $databaseName" }
    }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
}
