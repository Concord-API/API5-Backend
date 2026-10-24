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

Describe 'custom.ini' {
    BeforeAll {
        $settings = @{}
        $section = ''
        foreach ($line in Get-Content -Path (Join-Path $PSScriptRoot '..' 'grafana' 'custom.ini')) {
            if ($line -match '^\[(.+)\]$') {
                $section = $Matches[1]
            }
            elseif ($line -match '^([^=]+?)\s*=\s*(.*)$') {
                $settings["$section.$($Matches[1])"] = $Matches[2]
            }
        }
    }

    It 'escuta só em 127.0.0.1' {
        $settings['server.http_addr'] | Should -Be '127.0.0.1'
    }

    It 'desliga <Key>, que depende da internet' -TestCases @(
        @{ Key = 'analytics.reporting_enabled'; Value = 'false' },
        @{ Key = 'analytics.check_for_updates'; Value = 'false' },
        @{ Key = 'analytics.check_for_plugin_updates'; Value = 'false' },
        @{ Key = 'analytics.feedback_links_enabled'; Value = 'false' },
        @{ Key = 'news.news_feed_enabled'; Value = 'false' },
        @{ Key = 'plugins.preinstall_disabled'; Value = 'true' },
        @{ Key = 'plugins.public_key_retrieval_disabled'; Value = 'true' },
        @{ Key = 'plugins.plugin_admin_enabled'; Value = 'false' },
        @{ Key = 'snapshots.external_enabled'; Value = 'false' },
        @{ Key = 'security.disable_gravatar'; Value = 'true' },
        @{ Key = 'public_dashboards.enabled'; Value = 'false' }
    ) {
        $settings[$Key] | Should -Be $Value
    }

    It 'mantém uma conta só com <Key> = <Value>' -TestCases @(
        @{ Key = 'users.allow_sign_up'; Value = 'false' },
        @{ Key = 'users.allow_org_create'; Value = 'false' },
        @{ Key = 'auth.anonymous.enabled'; Value = 'false' },
        @{ Key = 'auth.basic.password_policy'; Value = 'true' }
    ) {
        $settings[$Key] | Should -Be $Value
    }
}

Describe 'Assert-GrafanaAdminPassword' {
    BeforeAll {
        function ConvertTo-Secret([string] $Value) {
            ConvertTo-SecureString -String $Value -AsPlainText -Force
        }
    }

    It 'aceita a senha que cumpre a política do Grafana' {
        { Assert-GrafanaAdminPassword -Password (ConvertTo-Secret 'Ratio-Monitor-2026') } | Should -Not -Throw
    }

    It 'recusa a senha com menos de 12 caracteres' {
        { Assert-GrafanaAdminPassword -Password (ConvertTo-Secret 'Ratio-2026') } | Should -Throw '*12 caracteres*'
    }

    It 'recusa a senha sem <Missing>' -TestCases @(
        @{ Missing = 'letra maiúscula'; Password = 'ratio-monitor-2026' },
        @{ Missing = 'letra minúscula'; Password = 'RATIO-MONITOR-2026' },
        @{ Missing = 'número'; Password = 'Ratio-Monitor-Obs' },
        @{ Missing = 'símbolo'; Password = 'RatioMonitor2026' }
    ) {
        { Assert-GrafanaAdminPassword -Password (ConvertTo-Secret $Password) } | Should -Throw "*$Missing*"
    }
}

Describe 'Read-GrafanaAdminPassword' {
    It 'devolve a senha digitada duas vezes' {
        Mock Read-Host -ModuleName Observability { ConvertTo-SecureString -String 'Ratio-Monitor-2026' -AsPlainText -Force }

        $password = Read-GrafanaAdminPassword

        [Net.NetworkCredential]::new('', $password).Password | Should -Be 'Ratio-Monitor-2026'
        Should -Invoke Read-Host -ModuleName Observability -Times 2 -Exactly
    }

    It 'recusa quando a confirmação não é igual à senha' {
        $script:answers = @('Ratio-Monitor-2026', 'Ratio-Monitor-2027')
        $script:reads = 0
        Mock Read-Host -ModuleName Observability {
            $answer = $script:answers[$script:reads]
            $script:reads++
            ConvertTo-SecureString -String $answer -AsPlainText -Force
        }

        { Read-GrafanaAdminPassword } | Should -Throw '*não são iguais*'
    }

    It 'recusa a senha fora da política' {
        Mock Read-Host -ModuleName Observability { ConvertTo-SecureString -String 'Ratio-2026' -AsPlainText -Force }

        { Read-GrafanaAdminPassword } | Should -Throw '*12 caracteres*'
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

Describe 'Add-PreloadLibrary' {
    It 'usa só o pg_stat_statements quando nada é pré-carregado' {
        Add-PreloadLibrary -Current '' -Library 'pg_stat_statements' | Should -Be 'pg_stat_statements'
    }

    It 'mantém as bibliotecas que já são pré-carregadas' {
        Add-PreloadLibrary -Current 'auto_explain' -Library 'pg_stat_statements' | Should -Be 'auto_explain,pg_stat_statements'
    }

    It 'não repete a biblioteca que já está na lista' {
        Add-PreloadLibrary -Current 'auto_explain, pg_stat_statements' -Library 'pg_stat_statements' |
            Should -Be 'auto_explain,pg_stat_statements'
    }
}

Describe 'Get-RandomPassword' {
    It 'gera uma senha de 32 caracteres só com letras e números' {
        Get-RandomPassword | Should -Match '^[A-Za-z0-9]{32}$'
    }

    It 'gera uma senha diferente a cada chamada' {
        Get-RandomPassword | Should -Not -Be (Get-RandomPassword)
    }
}

Describe 'Merge-EnvironmentEntry' {
    It 'acrescenta a variável às que o serviço já tem' {
        Merge-EnvironmentEntry -Entries @('ALLOY_DEPLOY_MODE=windows') -Name 'RATIO_MONITOR_DSN' -Value 'postgresql://x' |
            Should -Be @('ALLOY_DEPLOY_MODE=windows', 'RATIO_MONITOR_DSN=postgresql://x')
    }

    It 'troca o valor da variável que já existe' {
        Merge-EnvironmentEntry -Entries @('RATIO_MONITOR_DSN=antigo', 'OUTRA=1') -Name 'RATIO_MONITOR_DSN' -Value 'novo' |
            Should -Be @('OUTRA=1', 'RATIO_MONITOR_DSN=novo')
    }

    It 'funciona quando o serviço não tem nenhuma variável' {
        Merge-EnvironmentEntry -Entries @() -Name 'RATIO_MONITOR_DSN' -Value 'novo' | Should -Be @('RATIO_MONITOR_DSN=novo')
    }
}
