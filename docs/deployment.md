# Deploy

Este documento cobre o primeiro deploy em produção sem executar nada por você. Nenhum
recurso pago é criado automaticamente.

## Pré-requisitos

- Imagem .NET 8 buildada a partir do `Dockerfile` (ou `dotnet publish`).
- PostgreSQL 16+ gerenciado ou em container, com backup habilitado.
- Um proxy/plataforma que termine TLS (Render, Railway, Nginx, etc.).
- Chave de API gerada com pelo menos 32 caracteres.
- Acesso de saída à internet para `sei.rj.gov.br` (HTTPS 443).

## Variáveis de ambiente obrigatórias

| Variável | Exemplo | Obrigatória |
| --- | --- | --- |
| `ConnectionStrings__SeiMonitor` | `Host=...;Port=5432;Database=sei_monitor;Username=...;Password=...;SSL Mode=Require` | Sim (ou `DATABASE_URL`) |
| `DATABASE_URL` | `postgresql://usuario:senha@host/sei_monitor?sslmode=require` | Alternativa à connection string (Neon) |
| `SEI_DB_PASSWORD` / `PGPASSWORD` | senha do banco | Alternativa à connection string completa |
| `PGPASSFILE` | caminho do `pgpass.conf` | Alternativa para execução local; evite em containers |
| `Seguranca__ApiKey` | chave aleatória de 48 bytes | Sim em Production |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Sim |
| `AllowedHosts` | `sei-monitor.exemplo.gov.br` | Recomendada |
| `SeiMonitor__IntervalMinutes` | `30` | Não |
| `SeiMonitor__StartupDelaySeconds` | `15` | Não |
| `SeiMonitor__CycleTimeoutSeconds` | `1800` | Não |
| `Database__CommandTimeoutSeconds` | `30` | Não |

Em redes gerenciadas, use `SSL Mode=Require` (ou `VerifyFull` com CA) na connection
string. Dentro do Compose, `SSL Mode=Disable` é aceitável porque o tráfego fica na rede
interna do Docker.

## Opção A — Docker Compose (VM ou máquina local)

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

3. Aplique as migrations **manualmente** (veja a seção abaixo). A aplicação não altera
   o schema no start.

4. Suba a aplicação:

   ```bash
   docker compose up -d --build sei-rj-monitor
   ```

5. Valide:

   ```bash
   curl -fsS http://127.0.0.1:8080/health/live
   curl -fsS http://127.0.0.1:8080/health/ready
   curl -fsS -H "Authorization: Bearer $SEGURANCA_API_KEY" \
     -H 'Content-Type: application/json' \
     -H 'Accept: application/json, text/event-stream' \
     -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' \
     http://127.0.0.1:8080/mcp
   ```

## Opção B - Render

1. Use o **Neon** como PostgreSQL. O banco `sei_monitor`, com owner `sei_monitor_owner`,
   já está provisionado e com as migrations aplicadas.
2. Crie um **Web Service** do tipo Docker apontando para o repositório.
3. Configure:
   - **Health Check Path**: `/health/live`
   - **Environment**: `Production`
   - `DATABASE_URL` com a URL **pooled** do Neon:
     `neon connection-string production --database-name sei_monitor --role-name sei_monitor_owner --pooled`
   - `Seguranca__ApiKey` como secret (mínimo de 32 caracteres; o Render pode gerar)
   - `AllowedHosts` com o domínio `.onrender.com` atribuído (ou `*`)
4. O container respeita a variável `PORT` injetada pelo Render; localmente usa `8080`.
5. As migrations não rodam no start (a imagem de runtime não inclui o SDK nem
   `dotnet ef`). Aplique antes com `npm run db:migrate`.
6. Após o deploy, valide `/health/ready` e o handshake MCP.
7. Recomendações: instância única (o worker é sequencial), auto-deploy desabilitado em
   produção, backup diário do PostgreSQL e alerta para `/health/ready`.

## Opção C — Railway

1. Crie o serviço PostgreSQL e o serviço da aplicação a partir do `Dockerfile`.
2. Defina as variáveis da tabela acima; use a variável de conexão do PostgreSQL
   fornecida pela plataforma com `SSL Mode=Require`.
3. Configure o health check para `/health/live`.
4. Aplique as migrations manualmente antes de liberar tráfego.
5. Mantenha uma réplica. Escalar horizontalmente exige um lock distribuído que **não**
   está implementado.

## Migrations (procedimento controlado)

As migrations estão em `src/InfraStructure/Migrations`
(`20261125082055_InitialCreate`). Não há aplicação automática no start.

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
- O usuário da aplicação não precisa de permissão para alterar schema em runtime; o
  ideal é separar usuário de migração e usuário de aplicação.

## Health checks

| Endpoint | Uso | Dependências |
| --- | --- | --- |
| `/health/live` | Liveness do processo/orquestrador | Nenhuma |
| `/health/ready` | Prontidão para tráfego | PostgreSQL (timeout de 5 s) |

Resposta: `{"status":"Healthy"}` (200) ou `{"status":"Unhealthy"}` (503), sem detalhes
sensíveis. Use liveness para reinício automático e readiness para roteamento/alerta.

## HTTPS e proxy

A aplicação escuta HTTP em `8080`. Em produção:

- termine TLS no proxy/plataforma e encaminhe para `http://app:8080`;
- force redirect HTTP → HTTPS na borda;
- mantenha HSTS habilitado (a aplicação já envia em Production);
- não publique a porta do PostgreSQL.

Se o proxy não preservar o host original, ajuste `AllowedHosts` para o host interno
usado no health check ou mantenha `*` conscientemente (a aplicação emite warning).

## Primeiro deploy — checklist

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

## Rollback

1. Reverta a imagem para a tag anterior (o Compose usa `sei-rj-monitor:local`; em
   plataformas, use o deploy anterior).
2. Se a migration for incompatível com a versão anterior, restaure o backup.
3. Mantenha migrations compatíveis com N-1 para evitar downtime em rollback.

## O que este repositório não faz

- Não cria banco, usuário, DNS, TLS ou recursos pagos.
- Não executa deploy automaticamente.
- Não aplica migrations no start da aplicação.
- Não configura rate limit (faça no proxy).
