# MCP e Integração — SEI-RJ Monitor MCP

## O que é MCP neste contexto

O [Model Context Protocol (MCP)](https://modelcontextprotocol.io) é um protocolo aberto que padroniza como aplicações (especialmente modelos de linguagem e assistentes de IA) se conectam a ferramentas e fontes de dados. Neste sistema:

- O **SEI-RJ Monitor** atua como **servidor MCP**.
- Clientes compatíveis (MCP Inspector, IDEs com suporte a MCP, aplicações LLM) atuam como **clientes MCP**.
- O servidor expõe **ferramentas** (tools) que o cliente pode invocar por nome, passando argumentos estruturados.

O transporte utilizado é **Streamable HTTP** em `/mcp`, conforme implementado pelo pacote `ModelContextProtocol.Server` no ASP.NET Core.

## Transporte e endpoint confirmados

| Item | Valor |
|---|---|
| Transporte | Streamable HTTP |
| Endpoint MCP | `https://SEU_HOST/mcp` (localhost:56432 ou 8080 em desenvolvimento/Docker) |
| Método HTTP | POST para mensagens JSON-RPC; GET/SSE para respostas streaming quando o cliente solicitar `Accept: text/event-stream` |
| Versão do protocolo | Negociada no `initialize` (exemplo observado no smoke test: `2025-06-18`) |
| Autenticação | Chave de API via `Authorization: Bearer <chave>` ou `X-Api-Key: <chave>` |
| CORS | Desativado por padrão; ativado se `Seguranca:AllowedOrigins` estiver preenchido |

## Ferramentas MCP

O servidor expõe exatamente **quatro ferramentas**, definidas em `src/Presentation/SeiTools.cs` com o atributo `[McpServerTool]`. A classe é marcada com `[McpServerToolType]`.

### 1. consultar_processo

**Finalidade:** Consulta uma página pública de pesquisa processual do SEI-RJ, registra o resultado no banco local e informa o que mudou desde a consulta anterior.

**Parâmetros:**

| Parâmetro | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `url` | string | Sim | URL completa da página pública de consulta do processo no domínio `sei.rj.gov.br`. |

**Validações:**
- A URL é validada por `SeiLink.TryParse`: esquema HTTPS, host `sei.rj.gov.br` ou subdomínio, porta 443, caminho exato `/sei/modulos/pesquisa/md_pesq_processo_exibir.php`, sem userinfo.
- Se inválida, retorna falha `LinkInvalido`.

**Comportamento:**
- Baixa o HTML com proteções anti-SSRF (timeouts, limite de bytes, redirecionamentos revalidados, DNS seguro).

**Resposta (exemplo fictício):**
```json
{
  "fonte": "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123",
  "consultadoEmUtc": "2024-01-20T12:00:00Z",
  "processo": "SEI-260005/005504/2024",
  "tipo": "Processo",
  "dataGeracao": "01/01/2024",
  "interessados": "TESTE DA SILVA",
  "acessoRestrito": false,
  "protocolosDeclarados": 1,
  "quantidadeProtocolos": 2,
  "protocolosComRestricao": 0,
  "protocolos": [
    { "numero": "74146277", "natureza": "Documento", "tipo": null, "data": "15/01/2024", "dataInclusao": "15/01/2024 10:00", "unidade": "UNIDADE/TESTE", "acessoRestrito": false }
  ],
  "andamentosDeclarados": 3,
  "quantidadeMovimentacoes": 3,
  "unidadeAtual": "UNIDADE/TESTE",
  "ultimaMovimentacaoEm": "10/01/2024 10:00",
  "movimentacoes": [
    { "dataHora": "10/01/2024 09:00", "unidade": "UNIDADE/TESTE", "unidadeRelacionada": null, "tipoEvento": "Recebimento", "descricao": "Recebimento do processo", "detectadaEmUtc": "2024-01-20T12:00:00Z" }
  ],
  "monitoramento": {
    "processoId": 1,
    "consultaId": 1,
    "primeiraConsulta": false,
    "protocolosNovos": 0,
    "protocolosTotal": 2,
    "movimentacoesNovas": 1,
    "movimentacoesTotal": 3,
    "aviso": "1 movimentação(ões) nova(s) desde a última consulta registrada."
  },
  "observacao": "Somente informações efetivamente exibidas na consulta pública foram extraídas. Acesso restrito não é contornado."
}
```

### 2. historico_processo

**Finalidade:** Lê o histórico local de um processo já consultado, sem fazer nova requisição ao SEI-RJ.

**Parâmetros:**

| Parâmetro | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `numero` | string | Sim | Número do processo (ex.: `SEI-260005/005504/2024`). |

**Comportamento:**
- Carrega o processo e suas consultas/movimentações do banco local.
- Se não encontrado, retorna `{ encontrado: false }`.
- Se encontrado, retorna dados do processo, lista de consultas e lista de movimentações.

**Resposta (exemplo fictício):**
```json
{
  "encontrado": true,
  "numero": "SEI-260005/005504/2024",
  "tipo": "Processo",
  "dataGeracao": "01/01/2024",
  "interessados": "TESTE DA SILVA",
  "acessoRestrito": false,
  "primeiraConsultaUtc": "2024-01-20T10:00:00Z",
  "ultimaConsultaUtc": "2024-01-20T12:00:00Z",
  "totalConsultas": 2,
  "quantidadeMovimentacoes": 3,
  "consultas": [
    { "id": 1, "consultadoEmUtc": "2024-01-20T10:00:00Z", "quantidadeProtocolos": 0, "quantidadeMovimentacoes": 0, "unidadeAtual": null },
    { "id": 2, "consultadoEmUtc": "2024-01-20T12:00:00Z", "quantidadeProtocolos": 2, "quantidadeMovimentacoes": 3, "unidadeAtual": "UNIDADE/TESTE" }
  ],
  "movimentacoes": [

## Fluxo típico de uso

O cliente/LLM deve seguir esta sequência recomendada:

1. `validar_link_processo` — valida o link fornecido pelo usuário antes de consultar.
2. `consultar_processo` — consulta o processo, persiste o snapshot e retorna novidades.
3. `historico_processo` — lê o histórico local (sem nova consulta ao SEI).
4. `listar_processos_monitorados` — lista todos os processos com contagens.

## Como conectar um cliente MCP compatível

### MCP Inspector

```bash
npx @modelcontextprotocol/inspector@latest
```

1. Selecione **Streamable HTTP**.
2. Informe a URL: `https://SEU_HOST/mcp`.
3. Informe o cabeçalho de autenticação:
   - `Authorization: Bearer <Seguranca:ApiKey>`, ou
   - `X-Api-Key: <chave>`.
4. Execute `tools/list` para ver as 4 ferramentas disponíveis.
5. Execute `tools/call` com o nome da ferramenta e os argumentos.

### Smoke test por linha de comando (PowerShell)

O repositório inclui `scripts/mcp-tools.ps1`:

```powershell
$env:MCP_API_KEY = "sua-chave-de-32-plus-caracteres"
pwsh scripts/mcp-tools.ps1 -BaseUrl http://localhost:8080 -Step list
```

Passos disponíveis: `initialize`, `list`, `validate`, `consult`, `historico`, `listar`.

Exemplo de chamada `initialize` (JSON-RPC):
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-06-18",
    "capabilities": {},
    "clientInfo": { "name": "meu-cliente", "version": "1.0" }
  }
}
```

## Configuração de autenticação (sem expor segredos)

Defina a variável de ambiente `Seguranca__ApiKey` com uma chave aleatória de pelo menos 32 caracteres. Exemplo de geração (não use este valor em produção):

```bash
openssl rand -base64 48
```

No container/Docker Compose, configure via `.env` (não versionado):
```
SEGURANCA_API_KEY=<chave-aleatória>
```

Em `Development`, se `Seguranca:ApiKey` não estiver definida, a autenticação fica desativada e um aviso é emitido no log. Não exponha esse ambiente publicamente.

## O que o servidor fornece e o que é responsabilidade do cliente/LLM

**O servidor fornece:**
- Quatro ferramentas MCP com contratos estáveis.
- Autenticação por chave de API no endpoint `/mcp`.
- Respostas JSON estruturadas (camelCase, indentado).
- Tratamento de erros com `tipoFalha` para falhas de infraestrutura, processo não encontrado, layout desconhecido e necessidade de interação humana.
- Monitoramento automático periódico em background (detecção de novidades sem intervenção do cliente).

**Responsabilidade do cliente/LLM:**
- Fornecer URLs válidas da consulta pública do SEI-RJ.
- Interpretar as respostas JSON e apresentá-las ao usuário.
- Gerenciar a chave de API e enviá-la nas requisições MCP.
- Decidir quando chamar `historico_processo` vs `consultar_processo`.
- Tratar casos de `ProcessoNaoEncontrado` (link expirado) e `LayoutDesconhecido` (mudança no HTML do SEI).
- O cliente não recebe detalhes internos (host, IP, stack traces) em caso de erro; apenas mensagens genéricas.

## Limitações de compatibilidade confirmadas

- Exatamente 4 ferramentas MCP; não há recursos (resources) nem prompts expostos.
- Transporte apenas Streamable HTTP; não há stdio ou outro transporte configurado no código.
- Não há versionamento de ferramentas além do protocolo MCP negociado.
- Não há suporte a múltiplos usuários/escopos; uma única chave de API protege todas as ferramentas.
- Não há notificações proativas do servidor para clientes (o monitoramento grava no banco; o cliente deve consultar para saber de novidades).
- O smoke test usa `Mcp-Session-Id` para sessões; clientes devem suportar sessões MCP se necessário.

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — camada Presentation e ferramentas MCP.
- [Fluxo de execução](03-fluxo-de-execucao.md) — entrada de requisição MCP e autenticação.
- [Segurança](07-seguranca.md) — autenticação por chave de API, headers, CORS.
- [Configuração e deploy](09-configuracao-e-deploy.md) — variáveis `Seguranca__ApiKey`, endpoint, health checks.
- [Smoke test](scripts/mcp-tools.ps1) — script de validação das ferramentas MCP.
    { "dataHora": "10/01/2024 09:00", "unidade": "UNIDADE/TESTE", "unidadeRelacionada": null, "tipoEvento": "Recebimento", "descricao": "Recebimento do processo", "detectadaEmUtc": "2024-01-20T10:00:00Z", "consultaId": 1 }
  ]
}
```

### 3. listar_processos_monitorados

**Finalidade:** Lista os processos já consultados e registrados no banco local, do mais recente para o mais antigo, com contagem de consultas e movimentações.

**Parâmetros:** nenhum.

**Comportamento:**
- Projeta contagens diretamente no banco (`Consultas.Count`, `Movimentacoes.Count`).
- Ordenado por `UltimaConsultaUtc DESC, Id DESC`.

**Resposta (exemplo fictício):**
```json
{
  "quantidade": 2,
  "processos": [
    {
      "numero": "SEI-260005/005504/2024",
      "tipo": "Processo",
      "primeiraConsultaUtc": "2024-01-20T10:00:00Z",
      "ultimaConsultaUtc": "2024-01-20T12:00:00Z",
      "totalConsultas": 2,
      "totalMovimentacoes": 3,
      "unidadeAtual": "UNIDADE/TESTE"
    }
  ]
}
```

### 4. validar_link_processo

**Finalidade:** Valida se um link pertence à consulta pública do SEI-RJ e identifica o formato da URL. Não acessa o SEI-RJ.

**Parâmetros:**

| Parâmetro | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `url` | string | Sim | URL da consulta pública do SEI-RJ. |

**Comportamento:**
- Chama `SeiLink.TryParse`.
- Se inválido: `{ valido: false, erro }`.
- Se válido: `{ valido: true, host, caminho, possuiParametroDeConsulta, formatoParametro, observacao }`.

**Resposta (exemplo fictício):**
```json
{
  "valido": true,
  "host": "baixada.sei.rj.gov.br",
  "caminho": "/sei/modulos/pesquisa/md_pesq_processo_exibir.php",
  "possuiParametroDeConsulta": true,
  "formatoParametro": "token-opaco",
  "observacao": "O link foi validado estruturalmente. A disponibilidade dos dados só pode ser confirmada durante a consulta."
}
```
- Extrai metadados, protocolos e movimentações via parser AngleSharp.
- Persiste snapshot em PostgreSQL.
- Calcula novidades (protocolos e movimentações novos desde a consulta anterior).
- Na primeira consulta, nada é marcado como novo (linha de base).
- Em caso de falha de infraestrutura (banco), retorna `tipoFalha: "FalhaInfraestrutura"` com mensagem genérica.