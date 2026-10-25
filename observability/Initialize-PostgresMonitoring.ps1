#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PostgresBin,
    [Parameter(Mandatory)] [string] $PostgresService,
    [string] $PostgresHost = '127.0.0.1',
    [int] $Port = 5432,
    [string] $Database = 'ratio',
    [string] $AdminUser = 'postgres'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Observability.psm1') -Force

if ($null -eq (Get-Service -Name 'Alloy' -ErrorAction SilentlyContinue)) {
    throw 'O serviço do Alloy não existe. Rode o Install-Observability.ps1 antes deste script.'
}

$psql = Join-Path $PostgresBin 'psql.exe'
if (-not (Test-Path -Path $psql -PathType Leaf)) {
    throw "O psql.exe não foi encontrado em '$PostgresBin'."
}

$adminPassword = Read-Host "Senha do usuário $AdminUser do PostgreSQL" -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassword)
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
$connection = @('-h', $PostgresHost, '-p', $Port, '-U', $AdminUser, '-d', $Database, '-X', '-q', '-v', 'ON_ERROR_STOP=1')

try {
    $current = & $psql @connection -tAc 'SHOW shared_preload_libraries'
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível conectar ao banco '$Database' com o usuário $AdminUser."
    }

    $libraries = @("$current" -split ',' | ForEach-Object { $_.Trim() })
    if ($libraries -notcontains 'pg_stat_statements') {
        $preload = Add-PreloadLibrary -Current "$current" -Library 'pg_stat_statements'
        Write-Host "Ativando o pg_stat_statements; o serviço $PostgresService será reiniciado."
        & $psql @connection -c "ALTER SYSTEM SET shared_preload_libraries = '$preload'"
        if ($LASTEXITCODE -ne 0) {
            throw 'Não foi possível alterar o shared_preload_libraries.'
        }

        Restart-Service -Name $PostgresService
        $deadline = (Get-Date).AddSeconds(60)
        do {
            Start-Sleep -Seconds 2
            & $psql @connection -tAc 'SELECT 1' 2>$null | Out-Null
        } while ($LASTEXITCODE -ne 0 -and (Get-Date) -lt $deadline)

        if ($LASTEXITCODE -ne 0) {
            throw "O PostgreSQL não voltou depois de reiniciar o serviço $PostgresService."
        }
    }

    Write-Host 'Criando o papel ratio_monitor...'
    $monitorPassword = Get-RandomPassword
    $sql = "\set password '$monitorPassword'`n" + (Get-Content -Path (Join-Path $PSScriptRoot 'postgres\monitor.sql') -Raw)
    $sql | & $psql @connection -v "database=$Database" -f -
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível criar o papel ratio_monitor.'
    }

    $dsn = "postgresql://ratio_monitor:$monitorPassword@${PostgresHost}:$Port/${Database}?sslmode=disable"
    $environment = Merge-EnvironmentEntry -Entries (Get-ServiceEnvironment -Name 'Alloy') -Name 'RATIO_MONITOR_DSN' -Value $dsn
    Set-ServiceEnvironment -Name 'Alloy' -Entries $environment
    Restart-Service -Name 'Alloy'
}
finally {
    Remove-Item -Path Env:PGPASSWORD -ErrorAction SilentlyContinue
}

Write-Host 'Monitoramento do PostgreSQL pronto: pg_stat_statements ativo e o papel ratio_monitor criado.'
Write-Host 'A senha do ratio_monitor fica só na variável RATIO_MONITOR_DSN do serviço do Alloy.'
