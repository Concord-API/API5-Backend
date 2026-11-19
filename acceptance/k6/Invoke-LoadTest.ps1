[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [Parameter(Mandatory)] [string] $RemoteWriteUrl,
    [string] $K6Path,
    [string] $ScriptPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LoadTest.psm1') -Force

$k6 = Find-K6Executable -Path $K6Path
if (-not $ScriptPath) {
    $ScriptPath = Join-Path $PSScriptRoot 'load.ts'
}
if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
    throw "O script do k6 não foi encontrado em $ScriptPath. Informe -ScriptPath."
}

$run = Get-LoadTestArgument -ScriptPath (Resolve-Path -LiteralPath $ScriptPath).ProviderPath -BaseUrl $BaseUrl -RemoteWriteUrl $RemoteWriteUrl
foreach ($variable in $run.Environment.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($variable.Key, $variable.Value, 'Process')
}

Write-Host "Execução do k6: $($run.TestId). No Grafana da homologação, painel Ratio · Carga, escolha essa execução."
& $k6 @($run.Arguments)
exit $LASTEXITCODE
