# SEI-RJ Monitor — servidor MCP

Servidor [MCP](https://modelcontextprotocol.io) em **C#/.NET 8** que consulta a página
pública de processos do SEI-RJ, grava snapshots em **PostgreSQL** e informa o que mudou
entre consultas. Expõe quatro ferramentas MCP via **Streamable HTTP** em `/mcp`.

O projeto lê apenas o que a consulta pública já exibe. Não resolve CAPTCHA, não acessa
documentos restritos e não contorna autenticação do SEI.

## Recursos

- `consultar_processo` — baixa a página pública, extrai metadados, protocolos e
  movimentações, persiste o snapshot e devolve as novidades desde a consulta anterior.
- `historico_processo` — lê o histórico local, sem nova consulta ao SEI.
- `listar_processos_monitorados` — lista os processos registrados com contagens.
- `validar_link_processo` — valida host, caminho e formato de um link do SEI-RJ.
- Monitoramento periódico em background, com isolamento de falhas por processo e por ciclo.
- Autenticação por chave de API no endpoint MCP (obrigatória em produção).
- Defesa em profundidade contra SSRF: allowlist de host/caminho, revalidação de cada
  redirecionamento e bloqueio de IPs privados também na resolução DNS.
- Health checks de liveness (sem dependências) e readiness (PostgreSQL).
- Migrations EF Core aplicadas de forma controlada, sem alteração automática de schema.

## Estrutura

```
src/
  Domain/          agregado Processo, value objects e interfaces de repositório
  Application/     casos de uso, portas de rede/parse e opções de monitoramento
  InfraStructure/  EF Core + Npgsql, repositórios, HTTP seguro, parser AngleSharp
  CrossCutting/    composição de DI e configuração
  Presentation/    ASP.NET Core, ferramentas MCP, autenticação, health checks, host
  Test/XUnit/      testes de domínio, segurança, persistência, aplicação e monitoramento
docs/              arquitetura, segurança, deploy e operação
scripts/           smoke test do protocolo MCP
```

Documentação detalhada: [arquitetura](docs/architecture.md) ·
[segurança](docs/security.md) · [deploy](docs/deployment.md) ·
[operação](docs/operations.md).

## Documentação

A documentação completa do projeto está organizada em 12 documentos na pasta `docs/`:

| Documento | Descrição |
|---|---|
| [Visão geral](docs/01-visao-geral.md) | Objetivo, funcionalidades e escopo do projeto. |
| [Arquitetura](docs/02-arquitetura.md) | Arquitetura, camadas e responsabilidades. |
| [Fluxo de execução](docs/03-fluxo-de-execucao.md) | Fluxo de consulta e detecção de alterações. |
| [Banco de dados](docs/04-banco-de-dados.md) | Persistência, tabelas e relacionamentos. |
| [MCP e integração](docs/05-mcp-e-integracao.md) | Ferramentas MCP e integração com clientes. |
| [Monitoramento automático](docs/06-monitoramento-automatico.md) | Monitoramento periódico dos processos. |
| [Segurança](docs/07-seguranca.md) | Autenticação, validações e proteção. |
| [Testes](docs/08-testes.md) | Estratégia e execução. |
| [Configuração e deploy](docs/09-configuracao-e-deploy.md) | Instalação, configuração e publicação. |
| [Decisões arquiteturais](docs/10-decisoes-arquiteturais.md) | Decisões, alternativas e justificativas. |
| [Roadmap](docs/11-roadmap.md) | Evolução e funcionalidades planejadas. |
| [Glossário](docs/12-glossario.md) | Termos técnicos utilizados no projeto. |

Documentos preexistentes preservados: [architecture.md](docs/architecture.md), [security.md](docs/security.md), [deployment.md](docs/deployment.md), [operations.md](docs/operations.md), [audit-report.md](docs/audit-report.md).

## Requisitos

- .NET 8 SDK (ou superior)
- PostgreSQL 16+
- Acesso de rede a `sei.rj.gov.br`

## Execução local

1. Crie o banco e o usuário (exemplo):

   ```sql
   CREATE USER sei_monitor_app WITH PASSWORD 'defina_uma_senha';
   CREATE DATABASE sei_monitor OWNER sei_monitor_app;
   ```

2. Informe a senha por variável de ambiente (recomendado) ou a connection string completa:

   ```powershell
   # Opção 1: senha em variável de ambiente; host/porta/banco/usuário usam os padrões
   $env:SEI_DB_PASSWORD = "defina_uma_senha"
   dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
   ```

   ```powershell
   # Opção 2: connection string completa
   $env:ConnectionStrings__SeiMonitor = "Host=localhost;Port=5432;Database=sei_monitor;Username=sei_monitor_app;Password=defina_uma_senha"
   dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
   ```

   ```powershell
   # Opção 3: usar o arquivo de senhas do PostgreSQL (nada de senha no projeto)
   $env:PGPASSFILE = "$env:APPDATA\postgresql\pgpass.conf"
   dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
   ```

   Se `ConnectionStrings:SeiMonitor` não for definida, a aplicação tenta a senha em
   `Database:PasswordEnvironmentVariable`, `SEI_DB_PASSWORD`, `SEI_MONITOR_DB_PASSWORD`
   ou `PGPASSWORD`; sem senha explícita, usa `PGPASSFILE` ou o pgpass padrão do sistema
   (`%APPDATA%\postgresql\pgpass.conf` no Windows, `~/.pgpass` no Unix). O arquivo não é
   lido pela aplicação: apenas o caminho é passado ao Npgsql.

   Se a variável tiver outro nome, informe apenas o nome dela:

   ```powershell
   $env:Database__PasswordEnvironmentVariable = "MINHA_SENHA_DO_BANCO"
   ```

   Variáveis aceitas para a senha: a definida em
   `Database:PasswordEnvironmentVariable`, depois `SEI_DB_PASSWORD`,
   `SEI_MONITOR_DB_PASSWORD` e `PGPASSWORD`. Host, porta, banco, usuário e SSL podem ser
   definidos por `SEI_DB_HOST`, `SEI_DB_PORT`, `SEI_DB_NAME`, `SEI_DB_USER`,
   `SEI_DB_SSL_MODE` (ou `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`).

3. Aplique as migrations (procedimento controlado, veja [deploy](docs/deployment.md)):

   ```powershell
   dotnet ef database update `
     --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `
     --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj
   ```

4. Execute:

   ```powershell
   dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
   ```

   Em Development o endpoint MCP fica sem autenticação quando `Seguranca:ApiKey`
   não está definida. Não exponha esse ambiente publicamente.

5. Verifique:

   - `GET http://localhost:56432/` — status e resumo do último ciclo
   - `GET /health/live` — processo vivo
   - `GET /health/ready` — PostgreSQL acessível
   - `POST /mcp` — endpoint MCP

### Build bloqueado por processo em execução

Se `dotnet build` falhar com `O processo não pode acessar o arquivo ... porque ele está
sendo usado por outro processo`, há uma instância anterior rodando (`dotnet run` ou
`SeiRjMonitor.Presentation.exe`) segurando as DLLs em `bin/Debug`.

```powershell
powershell -NoProfile -File scripts/stop-dev.ps1
dotnet build SEI-RJ-Monitor.sln
```

O script encerra apenas processos deste projeto e não toca em outros `dotnet` da máquina.
Use `-Force` se a primeira tentativa não encerrar.

## Docker Compose

```bash
cp .env.example .env    # preencha POSTGRES_PASSWORD e SEGURANCA_API_KEY
docker compose up --build -d
docker compose ps
```

O `docker-compose.yml` usa os valores do `.env`, não publica a porta do PostgreSQL
(apenas `127.0.0.1`) e configura health checks para banco e aplicação. As migrations
**não** são aplicadas automaticamente no start; siga o procedimento em
[deploy](docs/deployment.md).

## Configuração

Todas as chaves podem ser definidas por variável de ambiente com `__` como separador
(ex.: `SeiMonitor__IntervalMinutes`).

| Chave | Padrão | Descrição |
| --- | --- | --- |
| `ConnectionStrings:SeiMonitor` | — | Connection string completa do PostgreSQL. Tem precedência sobre as variáveis isoladas. |
| `Database:PasswordEnvironmentVariable` | `SEI_DB_PASSWORD` (dev) | Nome da variável que contém a senha, usada quando não há connection string completa. |
| `SEI_DB_PASSWORD` / `PGPASSWORD` | — | Senha do banco quando a connection string não é informada. |
| `PGPASSFILE` | `%APPDATA%\postgresql\pgpass.conf` | Arquivo de senhas do PostgreSQL usado quando não há senha explícita. |
| `SEI_DB_HOST`, `SEI_DB_PORT`, `SEI_DB_NAME`, `SEI_DB_USER`, `SEI_DB_SSL_MODE` | `localhost`, `5432`, `sei_monitor`, `sei_monitor_app`, `Prefer` | Complementos opcionais quando a senha vem de variável. |
| `Seguranca:ApiKey` | — | Chave do endpoint MCP. Obrigatória em Production (mínimo 32 caracteres). |
| `Seguranca:ExigirAutenticacao` | `true` em Production | Exige a chave no `/mcp`. |
| `Seguranca:AllowedOrigins` | vazio | Origens CORS permitidas (vazio desativa CORS). |
| `SeiMonitor:IntervalMinutes` | `30` | Intervalo entre ciclos. `0` desativa o agendador. |
| `SeiMonitor:DelayBetweenProcessesSeconds` | `2` | Pausa entre processos no mesmo ciclo. |
| `SeiMonitor:CycleTimeoutSeconds` | `1800` | Tempo máximo de um ciclo. |
| `SeiMonitor:StartupDelaySeconds` | `10` | Espera antes do primeiro ciclo. |
| `SeiMonitor:Http:TimeoutSeconds` | `30` | Timeout total da requisição ao SEI. |
| `SeiMonitor:Http:MaxResponseBytes` | `5242880` | Tamanho máximo do HTML aceito. |
| `Database:CommandTimeoutSeconds` | `30` | Timeout de comandos SQL (5–120). |
| `AllowedHosts` | `*` | Em produção, defina o domínio real. |

## Ferramentas MCP

As ferramentas mantêm os nomes e contratos originais:

| Ferramenta | Parâmetro | Função |
| --- | --- | --- |
| `consultar_processo` | `url` | Consulta, persiste e devolve metadados, protocolos, histórico e novidades. |
| `historico_processo` | `numero` | Lê o histórico local do processo. |
| `listar_processos_monitorados` | — | Lista os processos registrados. |
| `validar_link_processo` | `url` | Valida o link sem acessar o SEI. |

Fluxo típico: `validar_link_processo` → `consultar_processo` →
`historico_processo` / `listar_processos_monitorados`.

### Conectar um cliente MCP

Transporte **Streamable HTTP** em `https://SEU_HOST/mcp`.

- `Authorization: Bearer <Seguranca:ApiKey>` **ou** `X-Api-Key: <chave>`

No MCP Inspector:

```bash
npx @modelcontextprotocol/inspector@latest
```

Selecione Streamable HTTP, informe a URL e o cabeçalho `Authorization: Bearer ...`.

Smoke test por linha de comando:

```powershell
$env:MCP_API_KEY = "sua-chave"
pwsh scripts/mcp-tools.ps1 -BaseUrl http://localhost:8080 -Step list
```

Passos disponíveis: `initialize`, `list`, `validate`, `consult`, `historico`, `listar`.

## Testes

```powershell
dotnet test src/Test/XUnit/SeiRjMonitor.Tests.csproj -c Release
```

A suíte cobre domínio/URLs, defesa SSRF, parser, autenticação, persistência (EF Core
InMemory e SQLite para constraints) e o ciclo de monitoramento (falhas, timeout,
cancelamento e não sobreposição). Nenhum teste acessa o SEI real ou exige PostgreSQL.

## Persistência e detecção de novidades

Quatro tabelas: `processos`, `consultas`, `protocolos` e `movimentacoes`.

- Cada consulta grava um snapshot em `consultas`.
- Protocolos e movimentações usam chave SHA-256 e índice único **por processo**
  (`processo_id, chave`), evitando duplicidade entre consultas.
- A primeira consulta é a linha de base: nada é marcado como novo.
- O agendador reutiliza o link da consulta mais recente de cada processo.

## Limitações conhecidas

- Não há busca por número sem link: a pesquisa do SEI exige CAPTCHA.
- Não há notificações externas (e-mail, webhook).
- Um link público pode expirar; nesse caso o processo passa a falhar até receber um
  link novo via `consultar_processo`.
- O monitoramento é sequencial por instância. Para muitos processos, ajuste o intervalo
  e o timeout do ciclo; veja [arquitetura](docs/architecture.md).
- O servidor não faz TLS. Em produção, termine TLS no proxy/plataforma.

## Licença

MIT — consulte [LICENSE.txt](LICENSE.txt).
