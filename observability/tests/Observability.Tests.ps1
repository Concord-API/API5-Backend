BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'Observability.psm1') -Force

    function New-Artifact([string] $Name, [string] $Content) {
        $path = Join-Path $TestDrive $Name
        Set-Content -Path $path -Value $Content -NoNewline
        $path
    }

    function Get-Sha256([string] $Path) {
        (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

Describe 'Test-ArtifactHash' {
    It 'aceita o artefato cujo SHA256 confere' {
        $artifact = New-Artifact 'artefato.zip' 'ratio'

        { Test-ArtifactHash -Path $artifact -Sha256 (Get-Sha256 $artifact) } | Should -Not -Throw
    }

    It 'aceita o SHA256 em maiúsculas' {
        $artifact = New-Artifact 'artefato.zip' 'ratio'

        { Test-ArtifactHash -Path $artifact -Sha256 (Get-Sha256 $artifact).ToUpperInvariant() } | Should -Not -Throw
    }

    It 'recusa o artefato cujo SHA256 não confere, citando o arquivo' {
        $artifact = New-Artifact 'artefato.zip' 'adulterado'

        { Test-ArtifactHash -Path $artifact -Sha256 ('0' * 64) } | Should -Throw '*artefato.zip*'
    }
}

Describe 'Assert-ArtifactFolder' {
    BeforeEach {
        $folder = Join-Path $TestDrive ([guid]::NewGuid())
        New-Item -ItemType Directory -Path $folder | Out-Null
        $first = Join-Path $folder 'primeiro.zip'
        $second = Join-Path $folder 'segundo.exe'
        Set-Content -Path $first -Value 'um' -NoNewline
        Set-Content -Path $second -Value 'dois' -NoNewline
        $manifest = @(
            [pscustomobject] @{ name = 'primeiro'; file = 'primeiro.zip'; sha256 = (Get-Sha256 $first) },
            [pscustomobject] @{ name = 'segundo'; file = 'segundo.exe'; sha256 = (Get-Sha256 $second) }
        )
    }

    It 'aceita a pasta com todos os artefatos do manifesto' {
        { Assert-ArtifactFolder -Manifest $manifest -ArtifactsPath $folder } | Should -Not -Throw
    }

    It 'recusa a pasta sem um dos artefatos, citando o que falta' {
        Remove-Item $second

        { Assert-ArtifactFolder -Manifest $manifest -ArtifactsPath $folder } | Should -Throw '*segundo.exe*'
    }

    It 'recusa a pasta com um artefato adulterado' {
        Set-Content -Path $second -Value 'trocado' -NoNewline

        { Assert-ArtifactFolder -Manifest $manifest -ArtifactsPath $folder } | Should -Throw '*segundo.exe*'
    }
}

Describe 'artifacts.json' {
    BeforeAll {
        $manifest = Get-ObservabilityManifest -Path (Join-Path $PSScriptRoot '..' 'artifacts.json')
    }

    It 'lista os cinco componentes da stack' {
        $manifest.name | Sort-Object | Should -Be @('alloy', 'grafana', 'loki', 'prometheus', 'winsw')
    }

    It 'traz URL https e SHA256 de 64 caracteres para cada artefato' {
        foreach ($artifact in $manifest) {
            $artifact.url | Should -Match '^https://'
            $artifact.sha256 | Should -Match '^[0-9a-f]{64}$'
        }
    }
}

Describe 'Wait-ComponentReady' {
    BeforeEach {
        Mock Start-Sleep -ModuleName Observability
    }

    It 'volta assim que o componente responde' {
        Mock Invoke-WebRequest -ModuleName Observability { [pscustomobject] @{ StatusCode = 200 } }

        { Wait-ComponentReady -Name 'Prometheus' -Uri 'http://127.0.0.1:9090/-/ready' -TimeoutSeconds 30 } | Should -Not -Throw
        Should -Invoke Invoke-WebRequest -ModuleName Observability -Times 1 -Exactly
    }

    It 'tenta de novo enquanto o componente ainda não responde' {
        $script:attempts = 0
        Mock Invoke-WebRequest -ModuleName Observability {
            $script:attempts++
            if ($script:attempts -lt 3) { throw 'Conexão recusada' }
            [pscustomobject] @{ StatusCode = 200 }
        }

        { Wait-ComponentReady -Name 'Loki' -Uri 'http://127.0.0.1:3100/ready' -TimeoutSeconds 30 } | Should -Not -Throw
        Should -Invoke Invoke-WebRequest -ModuleName Observability -Times 3 -Exactly
    }

    It 'desiste depois do tempo limite, citando o componente e o endereço' {
        Mock Invoke-WebRequest -ModuleName Observability { throw 'Conexão recusada' }

        { Wait-ComponentReady -Name 'Grafana' -Uri 'http://127.0.0.1:3000/api/health' -TimeoutSeconds 0 } |
            Should -Throw '*Grafana*http://127.0.0.1:3000/api/health*'
    }
}

Describe 'Invoke-Installer' {
    It 'aceita o instalador que termina com sucesso' {
        Mock Start-Process -ModuleName Observability { [pscustomobject] @{ ExitCode = 0 } }

        { Invoke-Installer -Name 'Alloy' -FilePath 'alloy-installer.exe' -ArgumentList '/S' } | Should -Not -Throw
    }

    It 'aceita o instalador que pede reinício' {
        Mock Start-Process -ModuleName Observability { [pscustomobject] @{ ExitCode = 3010 } }

        { Invoke-Installer -Name 'Grafana' -FilePath 'msiexec.exe' -ArgumentList '/i', 'grafana.msi', '/qn' } | Should -Not -Throw
    }

    It 'recusa o instalador que termina com erro, citando o componente e o código' {
        Mock Start-Process -ModuleName Observability { [pscustomobject] @{ ExitCode = 1603 } }

        { Invoke-Installer -Name 'Grafana' -FilePath 'msiexec.exe' -ArgumentList '/i', 'grafana.msi', '/qn' } |
            Should -Throw '*Grafana*1603*'
    }
}
