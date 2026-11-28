#requires -Version 5.1
<#
.SYNOPSIS
    Aplica as migrations EF Core no banco sei_monitor (owner sei_monitor_owner),
    usando a conexão direta (sem pooler), como recomendado pelo Neon.

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

$saida = neon connection-string $Branch --database-name $Database --role-name $Role --color false 2>&1 | Out-String
$url = ($saida -split "`r?`n" | Where-Object { $_ -match '^postgres(ql)?://' } | Select-Object -First 1)

if ([string]::IsNullOrWhiteSpace($url)) {
    throw "Não foi possível obter a connection string de $Database/$Role no branch $Branch."
}

$env:DATABASE_URL_UNPOOLED = $url.Trim()
dotnet ef database update --project src/InfraStructure --startup-project src/Presentation
