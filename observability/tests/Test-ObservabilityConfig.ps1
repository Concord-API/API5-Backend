[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$observability = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $observability 'Observability.psm1') -Force

function Invoke-ConfigCheck {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    Write-Host "Validando a configuração do $Name..."
    $ErrorActionPreference = 'Continue'
    & $FilePath @ArgumentList 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        throw "A configuração do $Name não passou na validação (código $LASTEXITCODE)."
    }
}

function Find-Executable {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Filter
    )

    $file = Get-ChildItem -Path $Path -Recurse -File -Filter $Filter | Where-Object { $_.Name -notlike '*uninstall*' } | Select-Object -First 1
    if ($null -eq $file) {
        throw "Nenhum '$Filter' encontrado em '$Path'."
    }

    $file.FullName
}

$manifest = @(Get-ObservabilityManifest -Path (Join-Path $observability 'artifacts.json') | Where-Object { $_.name -in 'prometheus', 'loki', 'alloy' })
Assert-ArtifactFolder -Manifest $manifest -ArtifactsPath $ArtifactsPath
$artifacts = @{}
foreach ($artifact in $manifest) {
    $artifacts[$artifact.name] = Join-Path $ArtifactsPath $artifact.file
}

$work = Join-Path ([IO.Path]::GetTempPath()) "ratio-observability-check-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Expand-Archive -Path $artifacts['prometheus'] -DestinationPath (Join-Path $work 'prometheus')
    $promtool = Find-Executable -Path (Join-Path $work 'prometheus') -Filter 'promtool.exe'
    Invoke-ConfigCheck -Name 'Prometheus' -FilePath $promtool -ArgumentList 'check', 'config', (Join-Path $observability 'prometheus\prometheus.yml')

    Expand-Archive -Path $artifacts['loki'] -DestinationPath (Join-Path $work 'loki')
    $loki = Find-Executable -Path (Join-Path $work 'loki') -Filter 'loki*.exe'
    $env:LOKI_DATA_DIR = Join-Path $work 'loki-data'
    Invoke-ConfigCheck -Name 'Loki' -FilePath $loki -ArgumentList "-config.file=$(Join-Path $observability 'loki\loki.yml')", '-config.expand-env=true', '-verify-config'

    & 7z x $artifacts['alloy'] "-o$(Join-Path $work 'alloy')" -y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível extrair o Alloy do instalador (7z terminou com $LASTEXITCODE)."
    }

    $alloy = Find-Executable -Path (Join-Path $work 'alloy') -Filter 'alloy*.exe'
    Invoke-ConfigCheck -Name 'Alloy' -FilePath $alloy -ArgumentList 'validate', (Join-Path $observability 'alloy\config.alloy')
}
finally {
    Remove-Item -Path Env:LOKI_DATA_DIR -ErrorAction SilentlyContinue
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Configurações do Prometheus, do Loki e do Alloy válidas.'
