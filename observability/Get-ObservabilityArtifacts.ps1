[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Destination
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Import-Module (Join-Path $PSScriptRoot 'Observability.psm1') -Force

$manifest = Get-ObservabilityManifest -Path (Join-Path $PSScriptRoot 'artifacts.json')
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

foreach ($artifact in $manifest) {
    $path = Join-Path $Destination $artifact.file
    if (Test-Path -Path $path -PathType Leaf) {
        try {
            Test-ArtifactHash -Path $path -Sha256 $artifact.sha256
            Write-Host "$($artifact.name) $($artifact.version): já baixado."
            continue
        }
        catch {
            Remove-Item -Path $path
        }
    }

    Write-Host "Baixando $($artifact.name) $($artifact.version)..."
    $partial = "$path.partial"
    Invoke-WebRequest -Uri $artifact.url -OutFile $partial -UseBasicParsing
    Move-Item -Path $partial -Destination $path -Force
    try {
        Test-ArtifactHash -Path $path -Sha256 $artifact.sha256
    }
    catch {
        Remove-Item -Path $path
        throw
    }
}

Assert-ArtifactFolder -Manifest $manifest -ArtifactsPath $Destination
Write-Host "Artefatos conferidos em '$Destination'."
