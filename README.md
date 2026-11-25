# SEI-RJ Monitor — MCP local

Servidor MCP em C#/.NET 8 para consultar informações efetivamente públicas exibidas na pesquisa processual do SEI-RJ, persistir snapshots em **PostgreSQL** e detectar novas movimentações entre consultas.

## Estrutura da solution (DDD)

```
solution.sln
src/
  01. Presentation/
    SeiRjMonitor.Presentation  (ASP.NET Core + servidor MCP + agendador + Program.cs)
  02. Application/
    SeiRjMonitor.Application   (casos de uso: consulta, monitoramento, persistência)
  03. Domain/
    SeiRjMonitor.Domain        (agregados DDD, value objects, interfaces de repositório)
  04. InfraStructure/
    SeiRjMonitor.Infrastructure (EF Core + Npgsql, DbContext, repositórios, serviços HTTP/parser)
  05. CrossCutting/
    SeiRjMonitor.CrossCutting  (DI/configuração centralizada)
  06. Test/
    SeiRjMonitor.Tests         (xUnit: testes de domínio e parser)
docs/
  sei-webservices-v3-1.pdf
scripts/
  mcp-tools.ps1                              (validação manual do protocolo MCP)
```

## Banco de dados (PostgreSQL)

A persistência foi migrada de SQLite para **PostgreSQL** usando EF Core 8 + `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.0.

A connection string é configurável via `ConnectionStrings:SeiMonitor` (appsettings.json ou variável de ambiente `ConnectionStrings__SeiMonitor`).

Exemplo em `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SeiMonitor": "Host=localhost;Port=5432;Database=sei_monitor;Username=sei_monitor_app;Password=SUA_SENHA"
  }
}
```

Exemplo em PowerShell:

```powershell
$env:ConnectionStrings__SeiMonitor = "Host=localhost;Port=5432;Database=sei_monitor;Username=sei_monitor_app;Password=SUA_SENHA"
```

O banco `sei_monitor` e o usuário `sei_monitor_app` devem existir previamente no PostgreSQL 18 instalado na máquina.

### Migrations (EF Core)

As migrations estão em `src\InfraStructure\Migrations` e são geradas pelo `IDesignTimeDbContextFactory`
(`SeiRjMonitorDbContextFactory`), que usa `ConnectionStrings__SeiMonitor` ou o fallback local.

Gerar uma nova migration:

```bash
dotnet ef migrations add NomeDaMigration --project src\InfraStructure\SeiRjMonitor.Infrastructure.csproj --startup-project src\Presentation\SeiRjMonitor.Presentation.csproj --output-dir Migrations
```

Aplicar no banco:

```bash
dotnet ef database update --project src\InfraStructure\SeiRjMonitor.Infrastructure.csproj --startup-project src\Presentation\SeiRjMonitor.Presentation.csproj
```

## Estado atual

Incluído:

- Servidor MCP via Streamable HTTP em `/mcp`.
- `ValidarLinkProcesso`: valida a URL e restringe host/caminho ao portal oficial.
- `ConsultarProcesso`: baixa a página pública, extrai metadados, protocolos e histórico, grava o snapshot e informa o que mudou desde a consulta anterior.
- `HistoricoProcesso`: devolve o histórico local de um processo já consultado, sem acessar o SEI.
- `ListarProcessosMonitorados`: lista os processos registrados, do mais recente para o mais antigo.
- Leitura posicional das tabelas reais do SEI (`#tblCabecalho`, `#tblDocumentos`, `#tblHistorico`), sem depender de `data-label`, que chega rotacionado no HTML.
- Decodificação explícita pelo charset do `Content-Type` (hoje `iso-8859-1`).
- Proteção básica contra SSRF: somente HTTPS em `sei.rj.gov.br` ou subdomínios e caminho `md_pesq_processo_exibir.php`.
- Indicação de acesso restrito (ícone `chave_restrito` no cabeçalho ou em protocolos).
- Respeito a conteúdo restrito: não abre anexos restritos, não resolve CAPTCHA e não contorna autenticação.
- Persistência PostgreSQL com detecção incremental de movimentações (deduplicação por hash SHA-256).
- Agendador multi-processo em background, com isolamento de falhas por processo.
- 12 testes xUnit passando (regras de domínio e parser com fixture HTML real).

Ainda não incluído:

- Busca por número sem URL (o fluxo de pesquisa exige CAPTCHA).
- Notificações externas (e-mail, webhook, push).
- Túnel/endpoint público para conectar o ChatGPT.
- Aplicação das migrations EF Core (aguardando connection string do usuário).
- Migração dos dados do SQLite existente (ou re-geração da baseline).

## Requisitos

- .NET 8 SDK ou superior.
- PostgreSQL 18 instalado e em execução na máquina.
- Acesso de rede ao portal público do SEI-RJ.

## Compilar e executar

Na raiz do repositório:

```bash
dotnet build solution.sln
dotnet run --project src/Presentation --launch-profile SeiRjMonitor.Presentation
```

O perfil de execução publica a aplicação em `http://localhost:8080`.

Alternativa sem o apphost `.exe` (útil quando o Windows bloqueia a execução):

```powershell
$env:ASPNETCORE_URLS = "http://localhost:8080"
$env:ConnectionStrings__SeiMonitor = "Host=localhost;Port=5432;Database=sei_monitor;Username=sei_app;Password=SUA_SENHA"
dotnet src\Presentation\bin\Debug\net8.0\SeiRjMonitor.Presentation.dll
```

Verifique o status:
`http://localhost:8080/`

Endpoint MCP:
`http://localhost:8080/mcp`

O `GET /` devolve o estado do agendador (habilitado, intervalo e resumo do último ciclo) e indica se a connection string foi configurada.

## Configuração

Configurações lidas da seção `SeiMonitor` e `ConnectionStrings`:

| Chave | Padrão | Descrição |
| --- | --- | --- |
| `ConnectionStrings:SeiMonitor` | (obrigatório) | Connection string do PostgreSQL. Ex: `Host=localhost;Port=5432;Database=sei_monitor;Username=sei_app;Password=...` |
| `SeiMonitor:IntervalMinutes` | `30` | Intervalo do agendador em minutos. `0` ou negativo desativa o monitoramento agendado. |

## Ferramentas MCP

| Ferramenta | Função |
| --- | --- |
| `validar_link_processo` | Valida estrutura, host e caminho de um link do SEI-RJ. |
| `consultar_processo` | Consulta, persiste e devolve metadados, protocolos, histórico e movimentações novas. |
| `historico_processo` | Lê o histórico local (consultas e movimentações) sem nova consulta ao SEI. |
| `listar_processos_monitorados` | Lista os processos já registrados com contagens. |

Fluxo típico: `validar_link_processo` → `consultar_processo` com o link completo → `historico_processo`/`listar_processos_monitorados` para acompanhar.

## Persistência e detecção de novidades

O banco PostgreSQL usa quatro tabelas: `processos`, `consultas`, `protocolos` e `movimentacoes` (chaves estrangeiras ativas).

- Cada consulta grava um registro em `consultas` e faz upsert de protocolos e movimentações.
- A deduplicação usa uma chave SHA-256 composta de `dataHora|unidade|unidadeRelacionada|tipoEvento|descricao`, com `UNIQUE(processo_id, chave)`.
- Na primeira consulta de um processo nada é marcado como novo (linha de base). A partir da segunda, `protocolosNovos`, `movimentacoesNovas` e `novasMovimentacoes` refletem apenas o que apareceu depois do snapshot anterior.
- O link usado pelo agendador é o da consulta mais recente registrada para o processo.

## Monitoramento agendado

`MonitoramentoBackgroundService` executa um ciclo imediato ao iniciar e depois a cada `SeiMonitor:IntervalMinutes`.

- Processos são consultados em ordem de última consulta (mais antigos primeiro), com pausa de 2 segundos entre eles.
- Um ciclo nunca se sobrepõe ao anterior.
- Falha em um processo é registrada e não interrompe os demais; o resumo do ciclo informa `processosConsultados` e `falhas`.
- Se o link público expirar, o processo passa a falhar com `ProcessoNaoEncontrado` até receber um link novo via `consultar_processo`.

## Testar com MCP Inspector

Em outro terminal:

```bash
npx @modelcontextprotocol/inspector@latest
```

Selecione transporte **Streamable HTTP** e informe:
`http://localhost:8080/mcp`

Execute primeiro `validar_link_processo`, depois `consultar_processo`, usando um link público completo do SEI-RJ.

## Executar testes

```bash
dotnet test src\Test\XUnit\SeiRjMonitor.Tests.csproj
```

## Observação sobre o ChatGPT

O servidor local pode ser desenvolvido e testado no computador. Para conectar o ChatGPT, será necessário um Secure MCP Tunnel ou um endpoint HTTPS remoto; não é necessário expor diretamente a máquina à internet.

## Próximos passos técnicos

1. Fornecer a connection string do PostgreSQL (usuário e senha) para criar o banco `sei_monitor`.
2. Aplicar `dotnet ef database update` para criar as tabelas.
3. Migrar os dados do SQLite existente (ou re-gerar a baseline).
4. Adicionar notificações quando `movimentacoesNovas > 0` (e-mail, webhook ou push).
5. Configurar túnel/endpoint público e documentar a conexão com o ChatGPT.
