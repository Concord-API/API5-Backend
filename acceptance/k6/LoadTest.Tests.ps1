BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'LoadTest.psm1') -Force

    function New-Artifact([string] $Name, [string] $Content) {
        $path = Join-Path $TestDrive $Name
        Set-Content -Path $path -Value $Content -NoNewline
        $path
    }
}

Describe 'Get-LoadTestArgument' {
    BeforeAll {
        $homologation = @{
            ScriptPath     = 'C:\k6\load.ts'
            BaseUrl        = 'http://homologacao:8080'
            RemoteWriteUrl = 'http://homologacao:8080/k6-metricas'
        }
    }

    It 'exige <Name>, sem valor padrão de servidor' -TestCases @(
        @{ Name = 'BaseUrl' },
        @{ Name = 'RemoteWriteUrl' }
    ) {
        $attribute = (Get-Command Get-LoadTestArgument).Parameters[$Name].Attributes |
            Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] }
        $attribute.Mandatory | Should -BeTrue
    }

    It 'manda as métricas ao Prometheus da homologação por remote write' {
        $run = Get-LoadTestArgument @homologation
        $index = [array]::IndexOf($run.Arguments, '-o')
        $run.Arguments[$index + 1] | Should -Be 'experimental-prometheus-rw'
        $run.Environment['K6_PROMETHEUS_RW_SERVER_URL'] | Should -Be 'http://homologacao:8080/k6-metricas'
    }

    It 'aponta o teste para a homologação' {
        $run = Get-LoadTestArgument @homologation
        $index = [array]::IndexOf($run.Arguments, '--env')
        $run.Arguments[$index + 1] | Should -Be 'BASE_URL=http://homologacao:8080'
    }

    It 'grava o P95, o P99, a média e o máximo do tempo de resposta' {
        $run = Get-LoadTestArgument @homologation
        $run.Environment['K6_PROMETHEUS_RW_TREND_STATS'] | Should -Be 'p(95),p(99),avg,max'
    }

    It 'marca a execução com o testid informado' {
        $run = Get-LoadTestArgument @homologation -TestId 'ensaio-1'
        $run.TestId | Should -Be 'ensaio-1'
        $index = [array]::IndexOf($run.Arguments, '--tag')
        $run.Arguments[$index + 1] | Should -Be 'testid=ensaio-1'
    }

    It 'gera o testid com a data e a hora quando nenhum é informado' {
        $run = Get-LoadTestArgument @homologation
        $run.TestId | Should -Match '^\d{8}-\d{6}$'
        $run.Arguments | Should -Contain "testid=$($run.TestId)"
    }

    It 'passa o script do k6 por último' {
        $run = Get-LoadTestArgument @homologation
        $run.Arguments[0] | Should -Be 'run'
        $run.Arguments[-1] | Should -Be 'C:\k6\load.ts'
    }
}

Describe 'Find-K6Executable' {
    It 'usa o caminho informado quando o arquivo existe' {
        $k6 = New-Artifact -Name 'k6.exe' -Content 'k6'
        Find-K6Executable -Path $k6 | Should -Be $k6
    }

    It 'recusa o caminho informado quando o arquivo não existe' {
        { Find-K6Executable -Path (Join-Path $TestDrive 'nao-existe.exe') } | Should -Throw '*nao-existe.exe*'
    }

    It 'procura o k6 no PATH quando nenhum caminho é informado' {
        Mock -ModuleName LoadTest Get-Command { [pscustomobject] @{ Source = 'C:\Tools\k6.exe' } } -ParameterFilter { $Name -eq 'k6' }
        Find-K6Executable | Should -Be 'C:\Tools\k6.exe'
    }

    It 'explica como resolver quando o k6 não está no PATH' {
        Mock -ModuleName LoadTest Get-Command { $null } -ParameterFilter { $Name -eq 'k6' }
        { Find-K6Executable } | Should -Throw '*-K6Path*'
    }
}

Describe 'Invoke-LoadTest.ps1' {
    BeforeAll {
        $script = Join-Path $PSScriptRoot 'Invoke-LoadTest.ps1'
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($script, [ref] $null, [ref] $null)
    }

    It 'tem ao lado o load.ts que roda por padrão' {
        Join-Path $PSScriptRoot 'load.ts' | Should -Exist
    }

    It 'exige <Name> ao rodar o teste' -TestCases @(
        @{ Name = 'BaseUrl' },
        @{ Name = 'RemoteWriteUrl' }
    ) {
        $parameter = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq $Name }
        $parameter | Should -Not -BeNullOrEmpty
        $parameter.Attributes.Extent.Text -join ' ' | Should -Match 'Mandatory'
    }

    It 'roda de fora da homologação, sem depender de nada do servidor' {
        $ast.Extent.Text | Should -Not -Match 'Observability\.psm1|Wait-ComponentReady|127\.0\.0\.1'
    }

    It 'não usa $PSScriptRoot nos valores padrão, que chega vazio no Windows PowerShell 5.1' {
        foreach ($parameter in $ast.ParamBlock.Parameters | Where-Object { $_.DefaultValue }) {
            $parameter.DefaultValue.Extent.Text | Should -Not -Match 'PSScriptRoot' -Because $parameter.Name.VariablePath.UserPath
        }
    }
}
