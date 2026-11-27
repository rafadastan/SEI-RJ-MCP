# Configuração e Deploy — SEI-RJ Monitor MCP

Este documento consolida informações de instalação, configuração, build, execução e orientação de deploy, baseadas exclusivamente em arquivos e configuração existentes no repositório.

## Pré-requisitos

- .NET 8 SDK (ou superior) para execução local ou build.
- PostgreSQL 16+ (gerenciado ou em container), com backup habilitado.
- Acesso de rede de saída para `sei.rj.gov.br` (HTTPS 443).
- Para Docker: Docker e Docker Compose.
- Uma proxy/plataforma que termine TLS em produção (Render, Railway, Nginx, etc.).

## Configuração local

### 1. Criar banco e usuário (exemplo SQL)

```sql
CREATE USER sei_monitor_app WITH PASSWORD 'defina_uma_senha';
CREATE DATABASE sei_monitor OWNER sei_monitor_app;
```

### 2. Informar a senha

Defina a senha por variável de ambiente (recomendado) ou a connection string completa:

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
# Opção 3: usar o arquivo de senhas do PostgreSQL
$env:PGPASSFILE = "$env:APPDATA\\postgresql\\pgpass.conf"
dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
```


## Configuração do PostgreSQL

A aplicação se conecta ao PostgreSQL via Npgsql. A connection string pode ser completa (`ConnectionStrings__SeiMonitor`) ou montada por variáveis isoladas. Em redes gerenciadas, use `SSL Mode=Require` (ou `VerifyFull` com CA) na connection string. Dentro do Docker Compose, `SSL Mode=Disable` é aceitável porque o tráfego fica na rede interna do Docker.

O usuário da aplicação não precisa de permissão para alterar schema em runtime; o ideal é separar usuário de migração e usuário de aplicação.

## Migrations

As migrations estão em `src/InfraStructure/Migrations` (`20261125082055_InitialCreate`). Não há aplicação automática no start.

### Aplicar com a CLI do EF Core (workstation com acesso ao banco)

```powershell
$env:ConnectionStrings__SeiMonitor = "Host=...;Port=5432;Database=sei_monitor;Username=...;Password=...;SSL Mode=Require"

dotnet ef database update `
  --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `
  --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj
```

### Gerar script idempotente (recomendado para banco gerenciado)

```powershell
dotnet ef migrations script --idempotent `
  --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `
  --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj `
  --output migrate.sql
```

Revise o SQL e aplique com um usuário de migração:

```bash
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f migrate.sql
```

Boas práticas:
- Faça backup antes de qualquer migration.
- Migrations devem ser aditivas e compatíveis com a versão anterior da aplicação.
- Não edite migrations já aplicadas; crie uma nova.
Se `ConnectionStrings:SeiMonitor` não for definida, a aplicação tenta a senha em `Database:PasswordEnvironmentVariable`, `SEI_DB_PASSWORD`, `SEI_MONITOR_DB_PASSWORD` ou `PGPASSWORD`; sem senha explícita, usa `PGPASSFILE` ou o pgpass padrão do sistema. O arquivo não é lido pela aplicação: apenas o caminho é passado ao Npgsql.

Variáveis aceitas para a senha: a definida em `Database:PasswordEnvironmentVariable`, depois `SEI_DB_PASSWORD`, `SEI_MONITOR_DB_PASSWORD` e `PGPASSWORD`. Host, porta, banco, usuário e SSL podem ser definidos por `SEI_DB_HOST`, `SEI_DB_PORT`, `SEI_DB_NAME`, `SEI_DB_USER`, `SEI_DB_SSL_MODE` (ou `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`).

### 3. Aplicar as migrations (procedimento controlado)

```powershell
dotnet ef database update `
  --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `

## Build

```powershell
dotnet build SEI-RJ-Monitor.sln -c Release
```

Se o build falhar com "arquivo bloqueado por outro processo", encerre instâncias anteriores do projeto:

```powershell
powershell -NoProfile -File scripts/stop-dev.ps1
dotnet build SEI-RJ-Monitor.sln
```

## Execução

```powershell
dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
```

A aplicação escuta em `http://localhost:56432` por padrão (conforme `launchSettings` do ASP.NET Core) ou na porta configurada pelo ambiente (`ASPNETCORE_URLS` / `PORT`).

## Endpoint


## Orientação de deploy (baseada em arquivos existentes)

### Opção A — Docker Compose (VM ou máquina local)

1. Copie e preencha o ambiente:

```bash
cp .env.example .env
# edite POSTGRES_PASSWORD, SEGURANCA_API_KEY e ALLOWED_HOSTS
```

2. Suba o banco e aguarde o health check:

```bash
docker compose up -d postgres
docker compose ps
```

3. Aplique as migrations **manualmente** (veja a seção acima). A aplicação não altera o schema no start.

4. Suba a aplicação:

```bash
docker compose up -d --build sei-rj-monitor
```

5. Valide:

```bash
curl -fsS http://127.0.0.1:8080/health/live
curl -fsS http://127.0.0.1:8080/health/ready
curl -fsS -H "Authorization: Bearer $SEGURANCA_API_KEY" \

## TLS/proxy

A aplicação não faz TLS. Em produção:
- Termine TLS no proxy/plataforma e encaminhe para `http://app:8080`.
- Force redirect HTTP → HTTPS na borda.
- Mantenha HSTS habilitado (a aplicação já envia em Production).
- Não publique a porta do PostgreSQL.

Se o proxy não preservar o host original, ajuste `AllowedHosts` para o host interno usado no health check ou mantenha `*` conscientemente (a aplicação emite warning).

## Checklist de implantação

- [ ] Banco criado com usuário dedicado e senha forte
- [ ] Backup inicial ou snapshot habilitado
- [ ] Migration `InitialCreate` revisada e aplicada
- [ ] `ConnectionStrings__SeiMonitor` configurada com TLS
- [ ] `Seguranca__ApiKey` configurada como secret (32+ caracteres)
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] `AllowedHosts` com o domínio real
- [ ] `SeiMonitor__IntervalMinutes` definido (0 para desativar o worker no início)
- [ ] TLS e redirect HTTPS ativos na borda
- [ ] `/health/live` respondendo 200
- [ ] `/health/ready` respondendo 200 após o banco
- [ ] `/mcp` retornando 401 sem chave e 200 com chave
- [ ] `tools/list` retornando exatamente 4 ferramentas
- [ ] Smoke test de `validar_link_processo` e `consultar_processo` com um link real
- [ ] Logs sem segredos e sem stack trace para o cliente
- [ ] Alerta configurado para readiness e para falhas repetidas do ciclo
- [ ] Rotina de backup testada (restauração em banco descartável)

## Solução de problemas frequentes

| Sintoma | Causa provável | Ação |
|---|---|---|
| `Connection string 'ConnectionStrings:SeiMonitor' não foi configurada` | Falta connection string ou senha | Defina `ConnectionStrings__SeiMonitor`, `SEI_DB_PASSWORD`/`PGPASSWORD` ou `PGPASSFILE`. |
| `Seguranca:ApiKey é obrigatória em produção` | Chave ausente em Production | Defina `Seguranca__ApiKey`. |
| `Seguranca:ApiKey deve ter pelo menos 32 caracteres` | Chave curta | Gere uma chave maior. |
| Container unhealthy | Porta 8080 incorreta ou `/health/live` não responde | Verifique a porta e o endpoint internamente. |
| `/mcp` retorna 401 | Chave incorreta ou ausente | Confirme cabeçalho e `Seguranca:ApiKey` no servidor. |
| `/health/ready` retorna 503 | PostgreSQL indisponível | Confirme banco, conexões, disco, firewall, senha. |
| Build falha com "arquivo bloqueado por outro processo" | Instância anterior rodando | Execute `scripts/stop-dev.ps1` e tente novamente. |
| Ciclos ignorados por sobreposição | Intervalo muito curto ou processos lentos | Aumente `IntervalMinutes` ou reduza a pausa. |
| `LayoutDesconhecido` | HTML do SEI mudou | Verifique o fixture e ajuste o parser. |

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — camadas e componentes.
- [Fluxo de execução](03-fluxo-de-execucao.md) — inicialização da aplicação.
- [Banco de dados](04-banco-de-dados.md) — migrations e connection string.
- [Segurança](07-seguranca.md) — autenticação, TLS, segredos.
- [MCP e integração](05-mcp-e-integracao.md) — endpoint e autenticação de clientes MCP.
- [Deploy preexistente](deployment.md) — procedimentos detalhados de Render, Railway e Compose.
- [Operações preexistentes](operations.md) — health checks, logs, runbook.
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' \
  http://127.0.0.1:8080/mcp
```

O `docker-compose.yml` usa os valores do `.env`, não publica a porta do PostgreSQL (apenas `127.0.0.1`) e configura health checks para banco e aplicação. As migrations **não** são aplicadas automaticamente no start.

### Opção B — Render

1. Crie um **PostgreSQL** gerenciado na mesma região.
2. Crie um **Web Service** do tipo Docker apontando para o repositório.
3. Configure:
   - **Health Check Path**: `/health/live`
   - **Environment**: `Production`
   - `ConnectionStrings__SeiMonitor` com `SSL Mode=Require` e os dados do banco
   - `Seguranca__ApiKey` como secret
   - `AllowedHosts` com o domínio `.onrender.com` atribuído
4. Aplique as migrations pelo procedimento manual (a imagem de runtime não inclui o SDK nem `dotnet ef`).
5. Após o deploy, valide `/health/ready` e o handshake MCP.
6. Recomendações: instância única (o worker é sequencial), auto-deploy desabilitado em produção, backup diário do PostgreSQL e alerta para `/health/ready`.

### Opção C — Railway

1. Crie o serviço PostgreSQL e o serviço da aplicação a partir do `Dockerfile`.
2. Defina as variáveis da tabela acima; use a variável de conexão do PostgreSQL fornecida pela plataforma com `SSL Mode=Require`.
3. Configure o health check para `/health/live`.
4. Aplique as migrations manualmente antes de liberar tráfego.
5. Mantenha uma réplica. Escalar horizontalmente exige um lock distribuído que **não** está implementado.
| Endpoint | Método | Descrição | Autenticação |
|---|---|---|---|
| `/` | GET | Status e resumo do último ciclo | Não |
| `/health/live` | GET | Liveness (sem dependências) | Não |
| `/health/ready` | GET | Readiness (PostgreSQL) | Não |
| `/mcp` | POST | Endpoint MCP Streamable HTTP | Sim (em Production) |

## Health checks

| Endpoint | Uso | Dependências |
|---|---|---|
| `/health/live` | Liveness do processo/orquestrador | Nenhuma |
| `/health/ready` | Prontidão para tráfego | PostgreSQL (timeout de 5 s) |

Resposta: `{"status":"Healthy"}` (200) ou `{"status":"Unhealthy"}` (503), sem detalhes sensíveis. Use liveness para reinício automático e readiness para roteamento/alerta.
  --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj
```

### 4. Executar

```powershell
dotnet run --project src/Presentation/SeiRjMonitor.Presentation.csproj
```

Em Development o endpoint MCP fica sem autenticação quando `Seguranca:ApiKey` não está definida. Não exponha esse ambiente publicamente.

### 5. Verificar

- `GET http://localhost:56432/` — status e resumo do último ciclo.
- `GET /health/live` — processo vivo.
- `GET /health/ready` — PostgreSQL acessível.
- `POST /mcp` — endpoint MCP.

## Variáveis/configurações confirmadas

Todas as chaves podem ser definidas por variável de ambiente com `__` como separador (ex.: `SeiMonitor__IntervalMinutes`).

| Chave | Padrão | Descrição |
|---|---|---|
| `ConnectionStrings:SeiMonitor` | — | Connection string completa do PostgreSQL. Tem precedência sobre as variáveis isoladas. |
| `Database:PasswordEnvironmentVariable` | `SEI_DB_PASSWORD` (dev) | Nome da variável que contém a senha, usada quando não há connection string completa. |
| `SEI_DB_PASSWORD` / `PGPASSWORD` | — | Senha do banco quando a connection string não é informada. |
| `PGPASSFILE` | `%APPDATA%\\postgresql\\pgpass.conf` | Arquivo de senhas do PostgreSQL usado quando não há senha explícita. |
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