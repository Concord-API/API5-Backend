#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Observability.psm1') -Force

if ($null -eq (Get-Service -Name 'Grafana' -ErrorAction SilentlyContinue)) {
    throw 'O serviço do Grafana não existe. Rode o Install-Observability.ps1 antes deste script.'
}

$password = Read-GrafanaAdminPassword
Set-GrafanaAdminPassword -GrafanaHome (Join-Path $env:ProgramFiles 'GrafanaLabs\grafana') -Password $password

Write-Host 'Senha do admin do Grafana trocada.'
