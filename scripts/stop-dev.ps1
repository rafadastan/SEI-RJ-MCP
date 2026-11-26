#requires -Version 5.1
<#
.SYNOPSIS
    Encerra instancias locais do SEI-RJ Monitor que bloqueiam DLLs em bin/obj.

.DESCRIPTION
    Um `dotnet run` ou o executavel SeiRjMonitor.Presentation.exe mantem as DLLs
    abertas em bin/Debug, o que faz o build falhar com "arquivo bloqueado por
    outro processo". Este script encerra apenas processos deste repositorio.

.EXAMPLE
    powershell -NoProfile -File scripts/stop-dev.ps1

.EXAMPLE
    powershell -NoProfile -File scripts/stop-dev.ps1 -Force
#>
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Get-ProcessosDoProjeto {
    Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and (
            $_.Name -eq 'SeiRjMonitor.Presentation.exe' -or
            (
                $_.Name -eq 'dotnet.exe' -and
                $_.CommandLine -and
                $_.CommandLine -match 'SeiRjMonitor\.Presentation\.csproj'
            )
        )
    }
}

$alvos = Get-ProcessosDoProjeto

if (-not $alvos) {
    Write-Host 'Nenhum processo do SEI-RJ Monitor em execucao.'
    exit 0
}

foreach ($processo in $alvos) {
    $descricao = "{0} (PID {1})" -f $processo.Name, $processo.ProcessId
    if ($Force) {
        Stop-Process -Id $processo.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "Encerrado: $descricao"
    }
    else {
        Stop-Process -Id $processo.ProcessId -ErrorAction SilentlyContinue
        Write-Host "Solicitado encerramento: $descricao"
    }
}

Start-Sleep -Seconds 1

$restantes = Get-ProcessosDoProjeto

if ($restantes) {
    Write-Warning 'Ainda ha processos ativos. Rode novamente com -Force.'
    foreach ($processo in $restantes) {
        Write-Warning ("  {0} (PID {1})" -f $processo.Name, $processo.ProcessId)
    }
    exit 1
}

Write-Host 'Pronto. Pode compilar: dotnet build SEI-RJ-Monitor.sln'
