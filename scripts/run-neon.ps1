#requires -Version 5.1
<#
.SYNOPSIS
    Executa a aplicação apontando para o banco sei_monitor (owner sei_monitor_owner)
    no branch production do Neon.

.DESCRIPTION
    A connection string é obtida em runtime com `neon connection-string` e injetada
    apenas na variável de ambiente do processo. Nenhum segredo é gravado em disco.
#>
param(
    [string]$Branch = 'production',
    [string]$Database = 'sei_monitor',
    [string]$Role = 'sei_monitor_owner'
)

$ErrorActionPreference = 'Stop'

$saida = neon connection-string $Branch --database-name $Database --role-name $Role --pooled --color false 2>&1 | Out-String
$url = ($saida -split "`r?`n" | Where-Object { $_ -match '^postgres(ql)?://' } | Select-Object -First 1)

if ([string]::IsNullOrWhiteSpace($url)) {
    throw "Não foi possível obter a connection string de $Database/$Role no branch $Branch."
}

$env:DATABASE_URL = $url.Trim()
dotnet run --project src/Presentation
