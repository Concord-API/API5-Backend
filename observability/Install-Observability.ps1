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
}
finally {
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}
