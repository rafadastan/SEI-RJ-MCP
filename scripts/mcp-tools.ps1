param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$ApiKey = $env:MCP_API_KEY,
    [string]$Url = 'https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?IC2o8Z7ACQH4LdQ4jJLJzjPBiLtP6l2FsQacllhUf-duzEubalut9yvd8-CzYYNLu7pd-wiM0k633-D6khhQNQrJ_mwMVuQDeMfHwt7nV51digBGG7p4DAZ3REb8yFhI',
    [string]$Numero = 'SEI-260005/005504/2024',
    [ValidateSet('initialize', 'list', 'validate', 'consult', 'historico', 'listar')]
    [string]$Step = 'list',
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$mcpUrl = "$BaseUrl/mcp"
$headers = @{
    'Accept' = 'application/json, text/event-stream'
    'Content-Type' = 'application/json'
}

if ($ApiKey) {
    $headers['X-Api-Key'] = $ApiKey
}
else {
    Write-Warning 'Nenhuma chave de API informada (-ApiKey ou MCP_API_KEY). Funciona apenas em ambiente de desenvolvimento sem autenticacao.'
}

function Invoke-Mcp {
    param([hashtable]$Payload, [string]$SessionId)

    $h = @{} + $headers
    if ($SessionId) { $h['Mcp-Session-Id'] = $SessionId }

    try {
        $response = Invoke-WebRequest -Uri $mcpUrl -Method Post -Headers $h `
            -Body ($Payload | ConvertTo-Json -Depth 20 -Compress) -UseBasicParsing
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 401) {
            throw 'HTTP 401: informe a chave de API com -ApiKey ou defina MCP_API_KEY.'
        }
        throw
    }

    $raw = $response.Content
    $session = $response.Headers['Mcp-Session-Id']

    $json = $raw -split "`n" |
        Where-Object { $_ -match '^\s*data:\s*' } |
        ForEach-Object { $_ -replace '^\s*data:\s*', '' } |
        Where-Object { $_.Trim() } |
        Select-Object -Last 1

    if (-not $json) { $json = $raw }

    [pscustomobject]@{
        Session = $session
        Json = $json | ConvertFrom-Json
    }
}

$init = Invoke-Mcp -Payload @{
    jsonrpc = '2.0'
    id = 1
    method = 'initialize'
    params = @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'sei-monitor-script'; version = '1.0' }
    }
}

Write-Host "initialize: $($init.Json.result.serverInfo.name) / $($init.Json.result.protocolVersion)" -ForegroundColor Green

if ($Step -eq 'initialize') { return }

$sessionId = $init.Session

$null = Invoke-Mcp -SessionId $sessionId -Payload @{
    jsonrpc = '2.0'
    method = 'notifications/initialized'
}

$list = Invoke-Mcp -SessionId $sessionId -Payload @{
    jsonrpc = '2.0'
    id = 2
    method = 'tools/list'
    params = @{}
}

$list.Json.result.tools | ForEach-Object {
    Write-Host ("tool: {0} ({1})" -f $_.name, (($_.inputSchema.required) -join ', ')) -ForegroundColor Cyan
}

if ($Step -eq 'list') { return }

$toolName = switch ($Step) {
    'validate' { 'validar_link_processo' }
    'consult' { 'consultar_processo' }
    'historico' { 'historico_processo' }
    'listar' { 'listar_processos_monitorados' }
}

$arguments = switch ($Step) {
    'validate' { @{ url = $Url } }
    'consult' { @{ url = $Url } }
    'historico' { @{ numero = $Numero } }
    'listar' { @{} }
}

$call = Invoke-Mcp -SessionId $sessionId -Payload @{
    jsonrpc = '2.0'
    id = 3
    method = 'tools/call'
    params = @{
        name = $toolName
        arguments = $arguments
    }
}

$text = $call.Json.result.content[0].text

if ($OutFile) {
    $text | Set-Content -Path $OutFile -Encoding UTF8
    Write-Host "resposta salva em $OutFile" -ForegroundColor DarkGray
}

if ($Step -eq 'consult') {
    $obj = $text | ConvertFrom-Json
    [pscustomobject]@{
        processo = $obj.processo
        tipo = $obj.tipo
        dataGeracao = $obj.dataGeracao
        acessoRestrito = $obj.acessoRestrito
        protocolosDeclarados = $obj.protocolosDeclarados
        quantidadeProtocolos = $obj.quantidadeProtocolos
        protocolosComRestricao = $obj.protocolosComRestricao
        andamentosDeclarados = $obj.andamentosDeclarados
        quantidadeMovimentacoes = $obj.quantidadeMovimentacoes
        unidadeAtual = $obj.unidadeAtual
        ultimaMovimentacaoEm = $obj.ultimaMovimentacaoEm
        processoId = $obj.monitoramento.processoId
        consultaId = $obj.monitoramento.consultaId
        primeiraConsulta = $obj.monitoramento.primeiraConsulta
        protocolosNovos = $obj.monitoramento.protocolosNovos
        movimentacoesNovas = $obj.monitoramento.movimentacoesNovas
        movimentacoesTotal = $obj.monitoramento.movimentacoesTotal
        aviso = $obj.monitoramento.aviso
    } | Format-List
}
elseif ($Step -eq 'listar') {
    $obj = $text | ConvertFrom-Json
    Write-Host "processos monitorados: $($obj.quantidade)" -ForegroundColor Green
    $obj.processos | Format-Table -AutoSize
}
elseif ($Step -eq 'historico') {
    $obj = $text | ConvertFrom-Json
    if (-not $obj.encontrado) {
        Write-Host $text -ForegroundColor Yellow
        return
    }

    [pscustomobject]@{
        numero = $obj.numero
        tipo = $obj.tipo
        totalConsultas = $obj.totalConsultas
        primeiraConsultaUtc = $obj.primeiraConsultaUtc
        ultimaConsultaUtc = $obj.ultimaConsultaUtc
        quantidadeMovimentacoes = $obj.quantidadeMovimentacoes
    } | Format-List
}
else {
    Write-Host $text
}
