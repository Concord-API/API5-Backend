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

function Stop-ServiceIfPresent {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Name
    )

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $service -and $service.Status -ne 'Stopped' -and $PSCmdlet.ShouldProcess($Name, 'Parar o serviço')) {
        Stop-Service -Name $Name -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(90))
    }
}

function Grant-LocalServiceWrite {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /grant '*S-1-5-19:(OI)(CI)M' /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível dar ao Serviço Local permissão de escrita em '$Path' (icacls terminou com $LASTEXITCODE)."
    }
}

function Register-WinSWService {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $WinSWPath
    )

    $wrapper = Join-Path $Directory "$Name.exe"
    Copy-Item -Path $WinSWPath -Destination $wrapper -Force
    if ($null -eq (Get-Service -Name $Name -ErrorAction SilentlyContinue)) {
        & $wrapper install | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "O WinSW não conseguiu registrar o serviço '$Name' (código $LASTEXITCODE)."
        }
    }

    Start-Service -Name $Name
}

function Invoke-Installer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        throw "O instalador do $Name terminou com o código $($process.ExitCode)."
    }
}

function Add-PreloadLibrary {
    [CmdletBinding()]
    param(
        [AllowEmptyString()] [string] $Current,
        [Parameter(Mandatory)] [string] $Library
    )

    $libraries = @($Current -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($libraries -notcontains $Library) {
        $libraries += $Library
    }

    $libraries -join ','
}

function Get-RandomPassword {
    [CmdletBinding()]
    param()

    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    $password = New-Object Text.StringBuilder
    $buffer = New-Object byte[] 1
    try {
        while ($password.Length -lt 32) {
            $generator.GetBytes($buffer)
            if ($buffer[0] -lt 248) {
                [void] $password.Append($alphabet[$buffer[0] % $alphabet.Length])
            }
        }
    }
    finally {
        $generator.Dispose()
    }

    $password.ToString()
}

function Assert-GrafanaAdminPassword {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [securestring] $Password
    )

    $plain = [Net.NetworkCredential]::new('', $Password).Password
    $missing = @()
    if ($plain.Length -lt 12) { $missing += 'pelo menos 12 caracteres' }
    if ($plain -cnotmatch '\p{Lu}') { $missing += 'letra maiúscula' }
    if ($plain -cnotmatch '\p{Ll}') { $missing += 'letra minúscula' }
    if ($plain -cnotmatch '\p{Nd}') { $missing += 'número' }
    if ($plain -cnotmatch '[\p{P}\p{S}]') { $missing += 'símbolo' }

    if ($missing.Count -gt 0) {
        throw "A senha do admin do Grafana não atende à política. Falta: $($missing -join ', ')."
    }
}

function Merge-EnvironmentEntry {
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [string[]] $Entries,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Value
    )

    $kept = @($Entries | Where-Object { $_ -and -not $_.StartsWith("$Name=") })
    $kept + "$Name=$Value"
}

function Get-ServiceEnvironment {
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)] [string] $Name
    )

    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    $value = Get-ItemProperty -Path $key -Name 'Environment' -ErrorAction SilentlyContinue
    if ($null -eq $value) {
        return [string[]] @()
    }

    [string[]] @($value.Environment)
}

function Set-ServiceEnvironment {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Entries
    )

    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    if ($PSCmdlet.ShouldProcess($Name, 'Gravar as variáveis de ambiente do serviço')) {
        New-ItemProperty -Path $key -Name 'Environment' -PropertyType MultiString -Value $Entries -Force | Out-Null
    }
}

Export-ModuleMember -Function Get-ObservabilityManifest, Test-ArtifactHash, Assert-ArtifactFolder, Wait-ComponentReady, Stop-ServiceIfPresent, Grant-LocalServiceWrite, Register-WinSWService, Invoke-Installer, Add-PreloadLibrary, Get-RandomPassword, Assert-GrafanaAdminPassword, Merge-EnvironmentEntry, Get-ServiceEnvironment, Set-ServiceEnvironment
