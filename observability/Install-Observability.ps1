#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'Observability.psm1') -Force

$installRoot = Join-Path $env:ProgramFiles 'Ratio\observability'
$dataRoot = Join-Path $env:ProgramData 'Ratio\observability'
$manifest = Get-ObservabilityManifest -Path (Join-Path $PSScriptRoot 'artifacts.json')
$artifacts = @{}
foreach ($artifact in $manifest) {
    $artifacts[$artifact.name] = Join-Path $ArtifactsPath $artifact.file
}

Write-Host 'Conferindo os artefatos...'
Assert-ArtifactFolder -Manifest $manifest -ArtifactsPath $ArtifactsPath

$staging = Join-Path ([IO.Path]::GetTempPath()) "ratio-observability-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Write-Host 'Instalando o Prometheus...'
    $prometheusHome = Join-Path $installRoot 'prometheus'
    Stop-ServiceIfPresent -Name 'ratio-prometheus'
    Grant-LocalServiceWrite -Path (Join-Path $dataRoot 'prometheus')
    New-Item -ItemType Directory -Path $prometheusHome -Force | Out-Null
    Expand-Archive -Path $artifacts['prometheus'] -DestinationPath (Join-Path $staging 'prometheus') -Force
    Copy-Item -Path (Join-Path $staging 'prometheus\*\*') -Destination $prometheusHome -Recurse -Force
    Copy-Item -Path (Join-Path $PSScriptRoot 'prometheus\prometheus.yml') -Destination $prometheusHome -Force
    Copy-Item -Path (Join-Path $PSScriptRoot 'prometheus\ratio-prometheus.xml') -Destination $prometheusHome -Force
    Register-WinSWService -Name 'ratio-prometheus' -Directory $prometheusHome -WinSWPath $artifacts['winsw']

    Write-Host 'Instalando o Loki...'
    $lokiHome = Join-Path $installRoot 'loki'
    Stop-ServiceIfPresent -Name 'ratio-loki'
    Grant-LocalServiceWrite -Path (Join-Path $dataRoot 'loki')
    New-Item -ItemType Directory -Path $lokiHome -Force | Out-Null
    Expand-Archive -Path $artifacts['loki'] -DestinationPath (Join-Path $staging 'loki') -Force
    Copy-Item -Path (Join-Path $staging 'loki\loki-windows-amd64.exe') -Destination (Join-Path $lokiHome 'loki.exe') -Force
    Copy-Item -Path (Join-Path $PSScriptRoot 'loki\loki.yml') -Destination $lokiHome -Force
    Copy-Item -Path (Join-Path $PSScriptRoot 'loki\ratio-loki.xml') -Destination $lokiHome -Force
    Register-WinSWService -Name 'ratio-loki' -Directory $lokiHome -WinSWPath $artifacts['winsw']

    Write-Host 'Instalando o Alloy...'
    $alloyConfig = Join-Path $dataRoot 'alloy\config.alloy'
    New-Item -ItemType Directory -Path (Split-Path -Parent $alloyConfig) -Force | Out-Null
    Copy-Item -Path (Join-Path $PSScriptRoot 'alloy\config.alloy') -Destination $alloyConfig -Force
    Invoke-Installer -Name 'Alloy' -FilePath $artifacts['alloy'] -ArgumentList '/S', "/CONFIG=$alloyConfig", '/DISABLEREPORTING=yes', '/DISABLEPROFILING=yes'
    Restart-Service -Name 'Alloy'

    Write-Host 'Instalando o Grafana...'
    Invoke-Installer -Name 'Grafana' -FilePath 'msiexec.exe' -ArgumentList '/i', "`"$($artifacts['grafana'])`"", '/qn', '/norestart'
    Copy-Item -Path (Join-Path $PSScriptRoot 'grafana\custom.ini') -Destination (Join-Path $env:ProgramFiles 'GrafanaLabs\grafana\conf\custom.ini') -Force
    Restart-Service -Name 'Grafana'
}
finally {
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Aguardando os componentes...'
Wait-ComponentReady -Name 'Prometheus' -Uri 'http://127.0.0.1:9090/-/ready'
Wait-ComponentReady -Name 'Loki' -Uri 'http://127.0.0.1:3100/ready'
Wait-ComponentReady -Name 'Alloy' -Uri 'http://127.0.0.1:12345/-/ready'
Wait-ComponentReady -Name 'Grafana' -Uri 'http://127.0.0.1:3000/api/health'

Write-Host ''
Write-Host 'Observabilidade instalada:'
foreach ($artifact in $manifest | Where-Object { $_.name -ne 'winsw' }) {
    Write-Host "  $($artifact.name) $($artifact.version)"
}
Write-Host 'Para ativar a telemetria da API, defina OpenTelemetry__Endpoint=http://127.0.0.1:4317 e reinicie o serviço da API.'
