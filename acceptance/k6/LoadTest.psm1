function Get-LoadTestArgument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [string] $BaseUrl = 'http://127.0.0.1',
        [string] $RemoteWriteUrl = 'http://127.0.0.1:9090/api/v1/write',
        [string] $TestId = (Get-Date -Format 'yyyyMMdd-HHmmss')
    )

    [pscustomobject] @{
        TestId      = $TestId
        Arguments   = [string[]] @('run', '--no-usage-report', '-o', 'experimental-prometheus-rw', '--tag', "testid=$TestId", '--env', "BASE_URL=$BaseUrl", $ScriptPath)
        Environment = [ordered] @{
            K6_PROMETHEUS_RW_SERVER_URL  = $RemoteWriteUrl
            K6_PROMETHEUS_RW_TREND_STATS = 'p(95),p(99),avg,max'
        }
    }
}

function Find-K6Executable {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [string] $Path
    )

    if ($Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            throw "O k6 não foi encontrado em $Path."
        }

        return $Path
    }

    $command = Get-Command -Name 'k6' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        throw 'O k6 não está no PATH. Instale o k6 (https://github.com/grafana/k6/releases) ou informe o caminho do k6.exe com -K6Path.'
    }

    $command.Source
}

Export-ModuleMember -Function Get-LoadTestArgument, Find-K6Executable
