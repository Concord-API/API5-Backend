BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'LoadTest.psm1') -Force

    function New-Artifact([string] $Name, [string] $Content) {
        $path = Join-Path $TestDrive $Name
        Set-Content -Path $path -Value $Content -NoNewline
        $path
    }
}

Describe 'Get-LoadTestArgument' {
    It 'manda as métricas ao Prometheus por remote write' {
        $run = Get-LoadTestArgument -ScriptPath 'C:\k6\load.ts'
        $index = [array]::IndexOf($run.Arguments, '-o')
        $run.Arguments[$index + 1] | Should -Be 'experimental-prometheus-rw'
        $run.Environment['K6_PROMETHEUS_RW_SERVER_URL'] | Should -Be 'http://127.0.0.1:9090/api/v1/write'
    }

    It 'usa o endereço de remote write informado' {
        $run = Get-LoadTestArgument -ScriptPath 'C:\k6\load.ts' -RemoteWriteUrl 'http://10.0.0.5:9090/api/v1/write'
        $run.Environment['K6_PROMETHEUS_RW_SERVER_URL'] | Should -Be 'http://10.0.0.5:9090/api/v1/write'
    }

    It 'grava o P95, o P99, a média e o máximo do tempo de resposta' {
        $run = Get-LoadTestArgument -ScriptPath 'C:\k6\load.ts'
        $run.Environment['K6_PROMETHEUS_RW_TREND_STATS'] | Should -Be 'p(95),p(99),avg,max'
    }

    It 'marca a execução com o testid informado' {
        $run = Get-LoadTestArgument -ScriptPath 'C:\k6\load.ts' -TestId 'ensaio-1'
        $run.TestId | Should -Be 'ensaio-1'
        $index = [array]::IndexOf($run.Arguments, '--tag')
        $run.Arguments[$index + 1] | Should -Be 'testid=ensaio-1'
    }

    It 'gera o testid com a data e a hora quando nenhum é informado' {
        $run = Get-LoadTestArgument -ScriptPath 'C:\k6\load.ts'
        $run.TestId | Should -Match '^\d{8}-\d{6}$'
        $run.Arguments | Should -Contain "testid=$($run.TestId)"
    }

    It 'aponta o teste para <Expected>' -TestCases @(
        @{ BaseUrl = $null; Expected = 'http://127.0.0.1' },
        @{ BaseUrl = 'http://homologacao'; Expected = 'http://homologacao' }
    ) {
        $parameters = @{ ScriptPath = 'C:\k6\load.ts' }
        if ($BaseUrl) {
            $parameters.BaseUrl = $BaseUrl
        }
        $run = Get-LoadTestArgument @parameters
        $index = [array]::IndexOf($run.Arguments, '--env')
        $run.Arguments[$index + 1] | Should -Be "BASE_URL=$Expected"
    }

    It 'passa o script do k6 por último' {
        $run = Get-LoadTestArgument -ScriptPath 'C:\k6\load.ts'
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
    It 'tem ao lado o load.ts que roda por padrão' {
        Join-Path $PSScriptRoot '..' 'load.ts' | Should -Exist
    }

    It 'não usa $PSScriptRoot nos valores padrão, que chega vazio no Windows PowerShell 5.1' {
        $script = Join-Path $PSScriptRoot '..' 'Invoke-LoadTest.ps1'
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($script, [ref] $null, [ref] $null)
        foreach ($parameter in $ast.ParamBlock.Parameters | Where-Object { $_.DefaultValue }) {
            $parameter.DefaultValue.Extent.Text | Should -Not -Match 'PSScriptRoot' -Because $parameter.Name.VariablePath.UserPath
        }
    }
}
