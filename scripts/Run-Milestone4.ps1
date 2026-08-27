$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $repositoryRoot 'artifacts\backend\logs'
$apiProcess = $null
$workerProcess = $null
$previousAspNetEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDotNetEnvironment = $env:DOTNET_ENVIRONMENT
$previousConnectionString = $env:SIMOPS_CONNECTION_STRING
$previousApiUrl = $env:SIMOPS_API_URL
$previousAdminKey = $env:SIMOPS_ADMIN_KEY

function Assert-CommandSucceeded {
    param([string]$Description)
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Start-BackendHost {
    param([string]$ProjectName, [string]$ExtraArguments = '')
    $assemblyPath = Join-Path $repositoryRoot "src\$ProjectName\bin\Release\net10.0\$ProjectName.dll"
    $arguments = ('"' + $assemblyPath + '" ' + $ExtraArguments).Trim()
    return Start-Process -FilePath 'dotnet' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logDirectory "$ProjectName.stdout.log") `
        -RedirectStandardError (Join-Path $logDirectory "$ProjectName.stderr.log")
}

try {
    $portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 5080)
    try {
        $portProbe.Start()
    }
    finally {
        $portProbe.Stop()
    }

    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    docker compose -f (Join-Path $repositoryRoot 'compose.yaml') -p simops up -d --wait postgres
    Assert-CommandSucceeded 'PostgreSQL startup'
    dotnet restore (Join-Path $repositoryRoot 'SimOps.slnx')
    Assert-CommandSucceeded 'Restore'
    dotnet build (Join-Path $repositoryRoot 'SimOps.slnx') -c Release --no-restore -m:1 -nodeReuse:false
    Assert-CommandSucceeded 'Build'

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:SIMOPS_CONNECTION_STRING = 'Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only;Maximum Pool Size=20'
    $env:SIMOPS_API_URL = 'http://127.0.0.1:5080'
    $env:SIMOPS_ADMIN_KEY = 'simops-local-dev-key'
    $apiProcess = Start-BackendHost 'SimOps.Api' '--urls http://127.0.0.1:5080'
    $workerProcess = Start-BackendHost 'SimOps.Worker'

    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($apiProcess.HasExited -or $workerProcess.HasExited) {
            throw "A backend host exited during startup. Inspect $logDirectory."
        }
        try {
            $response = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/health/ready' -TimeoutSec 2
            if ($response.status -eq 'ready') {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) {
        throw 'The API did not become ready within 30 seconds.'
    }

    dotnet run --project (Join-Path $repositoryRoot 'tests\SimOps.Backend.Specs') -c Release --no-build -- --http
    Assert-CommandSucceeded 'HTTP integration specs'

    Stop-Process -Id $workerProcess.Id
    $workerProcess.WaitForExit()
    $workerProcess = $null
    dotnet run --project (Join-Path $repositoryRoot 'tests\SimOps.Backend.Specs') -c Release --no-build -- --lease
    Assert-CommandSucceeded 'Lease recovery specs'

    dotnet run --project (Join-Path $repositoryRoot 'tests\SimOps.Game.Core.Specs') -c Release --no-build
    Assert-CommandSucceeded 'Game Core regression specs'
    dotnet run --project (Join-Path $repositoryRoot 'tests\SimOps.Agent.Specs') -c Release --no-build
    Assert-CommandSucceeded 'Agent regression specs'
    Write-Host 'Milestone 4 automation completed. PostgreSQL remains available; test API and Worker are stopped.'
}
finally {
    foreach ($ownedProcess in @($workerProcess, $apiProcess)) {
        if ($null -ne $ownedProcess -and -not $ownedProcess.HasExited) {
            Stop-Process -Id $ownedProcess.Id
            $ownedProcess.WaitForExit()
        }
    }
    $env:ASPNETCORE_ENVIRONMENT = $previousAspNetEnvironment
    $env:DOTNET_ENVIRONMENT = $previousDotNetEnvironment
    $env:SIMOPS_CONNECTION_STRING = $previousConnectionString
    $env:SIMOPS_API_URL = $previousApiUrl
    $env:SIMOPS_ADMIN_KEY = $previousAdminKey
}
