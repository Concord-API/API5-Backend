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

    It 'lista os seis componentes da stack' {
        $manifest.name | Sort-Object | Should -Be @('alloy', 'grafana', 'loki', 'prometheus', 'tempo', 'winsw')
    }

    It 'traz URL https e SHA256 de 64 caracteres para cada artefato' {
        foreach ($artifact in $manifest) {
            $artifact.url | Should -Match '^https://'
            $artifact.sha256 | Should -Match '^[0-9a-f]{64}$'
        }
    }
}

Describe 'tempo.yml' {
    BeforeAll {
        $path = Join-Path $PSScriptRoot '..' 'tempo' 'tempo.yml'
        $config = if (Test-Path -Path $path) { Get-Content -Path $path -Raw } else { '' }
    }

    It 'escuta só em 127.0.0.1 com <Key>' -TestCases @(
        @{ Key = 'http_listen_address'; Value = '127.0.0.1' },
        @{ Key = 'http_listen_port'; Value = '3200' },
        @{ Key = 'grpc_listen_address'; Value = '127.0.0.1' },
        @{ Key = 'grpc_listen_port'; Value = '9097' }
    ) {
        $config | Should -Match "(?m)^\s+$Key\s*:\s*$([regex]::Escape($Value))\s*$"
    }

    It 'recebe os traces do Alloy por OTLP em 127.0.0.1:4417' {
        $config | Should -Match '(?m)^\s+endpoint\s*:\s*127\.0\.0\.1:4417\s*$'
    }

    It 'guarda os traces por 15 dias no agendador e no worker' {
        [regex]::Matches($config, '(?m)^\s+block_retention\s*:\s*360h\s*$').Count | Should -Be 2
    }

    It 'grava os dados na pasta de TEMPO_DATA_DIR' {
        $config | Should -Match '(?m)^\s+path\s*:\s*\$\{TEMPO_DATA_DIR\}/wal\s*$'
        $config | Should -Match '(?m)^\s+path\s*:\s*\$\{TEMPO_DATA_DIR\}/blocks\s*$'
    }

    It 'não manda estatística de uso, que depende da internet' {
        $config | Should -Match '(?m)^\s+reporting_enabled\s*:\s*false\s*$'
    }
}

Describe 'config.alloy' {
    BeforeAll {
        $config = Get-Content -Path (Join-Path $PSScriptRoot '..' 'alloy' 'config.alloy') -Raw
    }

    It 'recebe os traces da API e os manda para o lote' {
        $receiver = [regex]::Match($config, '(?s)otelcol\.receiver\.otlp "api" \{.*?\n\}').Value
        $receiver | Should -Match 'traces\s*=\s*\[otelcol\.processor\.batch\.default\.input\]'
    }

    It 'manda os traces do lote para o Tempo' {
        $batch = [regex]::Match($config, '(?s)otelcol\.processor\.batch "default" \{.*?\n\}').Value
        $batch | Should -Match 'traces\s*=\s*\[otelcol\.exporter\.otlp\.tempo\.input\]'
    }

    It 'entrega ao Tempo em 127.0.0.1:4417, sem TLS' {
        $exporter = [regex]::Match($config, '(?s)otelcol\.exporter\.otlp "tempo" \{.*?\n\}').Value
        $exporter | Should -Match 'endpoint\s*=\s*"127\.0\.0\.1:4417"'
        $exporter | Should -Match 'insecure\s*=\s*true'
    }
}

Describe 'ratio-tempo.xml' {
    BeforeAll {
        $path = Join-Path $PSScriptRoot '..' 'tempo' 'ratio-tempo.xml'
        $service = if (Test-Path -Path $path) { ([xml] (Get-Content -Path $path -Raw)).service } else { $null }
    }

    It 'registra o serviço ratio-tempo' {
        $service.id | Should -Be 'ratio-tempo'
    }

    It 'roda o Tempo com a configuração ao lado, expandindo as variáveis' {
        $service.executable | Should -Be '%BASE%\tempo.exe'
        $service.arguments | Should -Be '-config.file="%BASE%\tempo.yml" -config.expand-env=true'
    }

    It 'grava os dados em ProgramData' {
        $dataDir = $service.env | Where-Object { $_.name -eq 'TEMPO_DATA_DIR' }
        $dataDir.value | Should -Be 'C:\ProgramData\Ratio\observability\tempo\data'
    }

    It 'roda como Serviço Local e sobe com o Windows' {
        $service.serviceaccount.domain | Should -Be 'NT AUTHORITY'
        $service.serviceaccount.user | Should -Be 'LocalService'
        $service.startmode | Should -Be 'Automatic'
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

    It 'abre na Visão geral instalada' {
        $settings['dashboards.default_home_dashboard_path'] | Should -Be 'C:\ProgramData\Ratio\observability\grafana\dashboards\visao-geral.json'
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

Describe 'fontes de dados do Grafana' {
    BeforeAll {
        $path = Join-Path $PSScriptRoot '..' 'grafana' 'provisioning' 'datasources' 'ratio.yaml'
        $sources = @{}
        $blocks = @{}
        if (Test-Path -Path $path) {
            foreach ($block in (Get-Content -Path $path -Raw) -split '(?m)^  -[ \t]+name:[ \t]*' | Select-Object -Skip 1) {
                $entry = @{}
                foreach ($match in [regex]::Matches($block, '(?m)^[ \t]*(?:-[ \t]+)?(\w+):[ \t]*(\S.*?)[ \t]*\r?$')) {
                    $value = $match.Groups[2].Value
                    if ($value -match "^'(.*)'$") {
                        $value = $Matches[1] -replace "''", "'"
                    }
                    if (-not $entry.ContainsKey($match.Groups[1].Value)) {
                        $entry[$match.Groups[1].Value] = $value
                    }
                }
                $name = ($block -split '\r?\n')[0].Trim()
                $sources[$name] = $entry
                $blocks[$name] = $block
            }
        }
    }

    It 'provisiona o Prometheus como fonte padrão em 127.0.0.1:9090' {
        $sources['Prometheus'].type | Should -Be 'prometheus'
        $sources['Prometheus'].uid | Should -Be 'prometheus'
        $sources['Prometheus'].url | Should -Be 'http://127.0.0.1:9090'
        $sources['Prometheus'].isDefault | Should -Be 'true'
    }

    It 'avisa o Grafana que o Prometheus recebe uma amostra por minuto' {
        $sources['Prometheus'].timeInterval | Should -Be '60s'
    }

    It 'provisiona o Loki em 127.0.0.1:3100' {
        $sources['Loki'].type | Should -Be 'loki'
        $sources['Loki'].uid | Should -Be 'loki'
        $sources['Loki'].url | Should -Be 'http://127.0.0.1:3100'
    }

    It 'provisiona o Tempo em 127.0.0.1:3200' {
        $sources['Tempo'].type | Should -Be 'tempo'
        $sources['Tempo'].uid | Should -Be 'tempo'
        $sources['Tempo'].url | Should -Be 'http://127.0.0.1:3200'
    }

    It 'abre no Loki os logs da API com o trace_id do trace' {
        $sources['Tempo'].datasourceUid | Should -Be 'loki'
        $sources['Tempo'].customQuery | Should -Be 'true'
        $sources['Tempo'].query | Should -Be '{service_name="ratio-api"} | trace_id="$${__trace.traceId}"'
    }

    It 'abre no Tempo o trace do trace_id de cada linha de log' {
        $sources['Loki'].datasourceUid | Should -Be 'tempo'
        $sources['Loki'].matcherType | Should -Be 'label'
        $sources['Loki'].matcherRegex | Should -Be 'trace_id'
        $blocks['Loki'] | Should -Match "(?m)^\s+url: '\`$\`$\{__value\.raw\}'\r?$"
    }

    It 'não deixa editar <Name> pela tela' -TestCases @(@{ Name = 'Prometheus' }, @{ Name = 'Loki' }, @{ Name = 'Tempo' }) {
        $sources[$Name].editable | Should -Be 'false'
    }
}

Describe 'painéis do Grafana' {
    BeforeAll {
        function Get-Panel([object[]] $Items) {
            foreach ($item in $Items) {
                $item
                if ($item.PSObject.Properties['panels']) {
                    Get-Panel $item.panels
                }
            }
        }

        $folder = Join-Path $PSScriptRoot '..' 'grafana' 'dashboards'
        $dashboards = @(Get-ChildItem -Path $folder -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
                [pscustomobject] @{ File = $_.Name; Json = Get-Content -Path $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
            })
        $targets = @(foreach ($dashboard in $dashboards) {
                foreach ($panel in Get-Panel $dashboard.Json.panels) {
                    if ($panel.PSObject.Properties['targets']) {
                        foreach ($target in $panel.targets) {
                            [pscustomobject] @{ Where = "$($dashboard.File) / $($panel.title)"; Target = $target }
                        }
                    }
                }
            })
        $expressions = @($targets | Where-Object { $_.Target.PSObject.Properties['expr'] })
    }

    It 'tem os cinco painéis' {
        $dashboards.File | Sort-Object | Should -Be @('api.json', 'banco.json', 'dados.json', 'servidor.json', 'visao-geral.json')
    }

    It 'dá a cada painel um uid único que começa com ratio-' {
        $uids = $dashboards | ForEach-Object { $_.Json.uid }
        $uids | ForEach-Object { $_ | Should -Match '^ratio-' }
        @($uids | Sort-Object -Unique).Count | Should -Be $dashboards.Count
    }

    It 'consulta só as fontes prometheus, loki e tempo' {
        $targets.Count | Should -BeGreaterThan 0
        foreach ($item in $targets) {
            $item.Target.datasource.uid | Should -BeIn @('prometheus', 'loki', 'tempo') -Because $item.Where
        }
    }

    It 'lista no painel API os traces de <Title>' -TestCases @(
        @{ Title = 'Requisições acima de 1 s'; Query = '{resource.service.name="ratio-api" && kind=server && duration > 1s}' },
        @{ Title = 'Requisições com erro'; Query = '{resource.service.name="ratio-api" && kind=server && status=error}' }
    ) {
        $item = $targets | Where-Object { $_.Where -eq "api.json / $Title" }
        $item.Target.datasource.uid | Should -Be 'tempo'
        $item.Target.queryType | Should -Be 'traceql'
        $item.Target.query | Should -Be $Query
    }

    It 'usa $__rate_interval em todo rate' {
        foreach ($item in $expressions) {
            $item.Target.expr | Should -Not -Match '\b(i?rate)\([^\]]*\[\d+[smhd]\]' -Because $item.Where
        }
    }

    It 'deixa as rotas de health fora das métricas HTTP' {
        foreach ($item in $expressions | Where-Object { $_.Target.expr -match 'http_server_request_duration_seconds' }) {
            $item.Target.expr | Should -Match 'http_route!~"health\.\*"' -Because $item.Where
        }
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

Describe 'Expand-TarArchive' {
    BeforeEach {
        $source = Join-Path $TestDrive ([guid]::NewGuid())
        New-Item -ItemType Directory -Path $source | Out-Null
        Set-Content -Path (Join-Path $source 'tempo.exe') -Value 'binário' -NoNewline
        $archive = Join-Path $TestDrive "$([guid]::NewGuid()).tar.gz"
        & tar -czf $archive -C $source 'tempo.exe'
        $destination = Join-Path $TestDrive ([guid]::NewGuid())
    }

    It 'extrai o tar.gz na pasta de destino, criando a pasta' {
        Expand-TarArchive -Path $archive -DestinationPath $destination

        Get-Content -Path (Join-Path $destination 'tempo.exe') -Raw | Should -Be 'binário'
    }

    It 'recusa o arquivo que não é tar.gz, citando o arquivo' {
        $broken = New-Artifact 'quebrado.tar.gz' 'não é tar'

        { Expand-TarArchive -Path $broken -DestinationPath $destination } | Should -Throw '*quebrado.tar.gz*'
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
