[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $K6Path,
    [Parameter(Mandatory)] [string] $ScriptPath,
    [string] $BaseUrl = 'http://127.0.0.1',
    [string] $PrometheusUrl = 'http://127.0.0.1:9090'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Observability.psm1') -Force

foreach ($path in $K6Path, $ScriptPath) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Arquivo não encontrado: $path"
    }
}

Wait-ComponentReady -Name 'Prometheus' -Uri "$PrometheusUrl/-/ready" -TimeoutSeconds 30

$run = Get-LoadTestArgument -ScriptPath (Resolve-Path -LiteralPath $ScriptPath).ProviderPath -BaseUrl $BaseUrl -RemoteWriteUrl "$PrometheusUrl/api/v1/write"
foreach ($variable in $run.Environment.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($variable.Key, $variable.Value, 'Process')
}

Write-Host "Execução do k6: $($run.TestId). No Grafana, painel Ratio · Carga, escolha essa execução."
& $K6Path @($run.Arguments)
exit $LASTEXITCODE
