[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [string] $K6Path,
    [string] $ScriptPath
)

$ErrorActionPreference = 'Stop'

if (-not $K6Path) {
    $command = Get-Command -Name 'k6' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        throw 'O k6 não está no PATH. Instale o k6 (https://github.com/grafana/k6/releases) ou informe o caminho do k6.exe com -K6Path.'
    }
    $K6Path = $command.Source
}
elseif (-not (Test-Path -LiteralPath $K6Path -PathType Leaf)) {
    throw "O k6 não foi encontrado em $K6Path."
}

if (-not $ScriptPath) {
    $ScriptPath = Join-Path $PSScriptRoot 'load.ts'
}
if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
    throw "O script do k6 não foi encontrado em $ScriptPath. Informe -ScriptPath."
}

$BaseUrl = $BaseUrl.TrimEnd('/')
$testId = Get-Date -Format 'yyyyMMdd-HHmmss'
$env:K6_PROMETHEUS_RW_SERVER_URL = "$BaseUrl/k6-metricas"
$env:K6_PROMETHEUS_RW_TREND_STATS = 'p(95),p(99),avg,max'

Write-Host "Execução do k6: $testId. No Grafana da homologação, painel Ratio · Carga, escolha essa execução."
& $K6Path run --no-usage-report -o experimental-prometheus-rw --tag "testid=$testId" --env "BASE_URL=$BaseUrl" (Resolve-Path -LiteralPath $ScriptPath).ProviderPath
exit $LASTEXITCODE
