[CmdletBinding()]
param(
    [string] $K6Path,
    [string] $ScriptPath,
    [string] $BaseUrl = 'http://127.0.0.1',
    [string] $PrometheusUrl = 'http://127.0.0.1:9090'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LoadTest.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'observability/Observability.psm1') -Force

$k6 = Find-K6Executable -Path $K6Path
if (-not $ScriptPath) {
    $ScriptPath = Join-Path $PSScriptRoot 'load.ts'
}
if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
    throw "O script do k6 não foi encontrado em $ScriptPath. Rode a partir de um clone do API5-Backend ou informe -ScriptPath."
}

Wait-ComponentReady -Name 'Prometheus' -Uri "$PrometheusUrl/-/ready" -TimeoutSeconds 30

$run = Get-LoadTestArgument -ScriptPath (Resolve-Path -LiteralPath $ScriptPath).ProviderPath -BaseUrl $BaseUrl -RemoteWriteUrl "$PrometheusUrl/api/v1/write"
foreach ($variable in $run.Environment.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($variable.Key, $variable.Value, 'Process')
}

Write-Host "Execução do k6: $($run.TestId). No Grafana, painel Ratio · Carga, escolha essa execução."
& $k6 @($run.Arguments)
exit $LASTEXITCODE
