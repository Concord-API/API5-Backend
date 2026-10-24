Set-StrictMode -Version 3.0

function Get-ObservabilityManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Test-ArtifactHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Sha256
    )

    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) {
        throw "O arquivo '$(Split-Path -Leaf $Path)' não confere com o SHA256 do manifesto: esperado $($Sha256.ToLowerInvariant()), obtido $($actual.ToLowerInvariant()). Baixe o artefato de novo a partir da URL oficial."
    }
}

function Assert-ArtifactFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]] $Manifest,
        [Parameter(Mandatory)] [string] $ArtifactsPath
    )

    foreach ($artifact in $Manifest) {
        $path = Join-Path $ArtifactsPath $artifact.file
        if (-not (Test-Path -Path $path -PathType Leaf)) {
            throw "Artefato ausente: '$($artifact.file)' não está em '$ArtifactsPath'."
        }

        Test-ArtifactHash -Path $path -Sha256 $artifact.sha256
    }
}

function Wait-ComponentReady {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Uri,
        [int] $TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Write-Verbose "$Name ainda não respondeu: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    throw "$Name não ficou pronto em $TimeoutSeconds s ($Uri). Veja o log do serviço."
}

Export-ModuleMember -Function Get-ObservabilityManifest, Test-ArtifactHash, Assert-ArtifactFolder, Wait-ComponentReady
