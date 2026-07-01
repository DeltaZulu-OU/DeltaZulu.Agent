param(
    [string]$Configuration = "Debug",
    [string]$Framework = "net10.0-windows"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

Set-Location $repoRoot

$project = "src/DeltaZulu.Agent.Daemon/DeltaZulu.Agent.Daemon.csproj"
$agentConfig = "config/dzagent.yaml"
$collectorConfig = "config/dzcollector.yaml"
$exePath = Join-Path $repoRoot "src/DeltaZulu.Agent.Daemon\bin\$Configuration\$Framework\dzagentd.exe"

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $agentConfig)) { throw "Agent config not found: $agentConfig" }
if (-not (Test-Path $collectorConfig)) { throw "Collector config not found: $collectorConfig" }

Write-Host "Building daemon once..." -ForegroundColor Cyan
& dotnet build $project -c $Configuration -f $Framework
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $exePath)) {
    throw "Built executable not found: $exePath"
}

function Start-DaemonWindow {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$ConfigPath
    )

    $command = @"
`$Host.UI.RawUI.WindowTitle = '$Title'
Set-Location '$repoRoot'
& '$exePath' '$ConfigPath'
Write-Host ''
Write-Host 'Process exited. Press Enter to close.' -ForegroundColor Yellow
[void][System.Console]::ReadLine()
"@

    Start-Process powershell.exe -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-Command", $command
    ) -WorkingDirectory $repoRoot
}

Write-Host "Starting collector..." -ForegroundColor Cyan
Start-DaemonWindow -Title "DeltaZulu Collector" -ConfigPath $collectorConfig

Start-Sleep -Seconds 2

Write-Host "Starting agent..." -ForegroundColor Cyan
Start-DaemonWindow -Title "DeltaZulu Agent" -ConfigPath $agentConfig

Write-Host ""
Write-Host "Started both roles from the same built executable." -ForegroundColor Green
Write-Host "Collector: $collectorConfig"
Write-Host "Agent:     $agentConfig"