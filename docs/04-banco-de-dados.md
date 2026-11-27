# Banco de Dados — SEI-RJ Monitor MCP

## Tecnologia e configuração

O sistema utiliza **PostgreSQL 16+** como banco de dados durável, acessado por **Entity Framework Core 8** com o provedor **Npgsql**.

### Configuração da connection string

A conexão é resolvida em `src/CrossCutting/Ioc/DependencyInjection.cs` (`MontarConnectionStringPorVariaveis`), com a seguinte ordem de precedência:

1. `ConnectionStrings:SeiMonitor` (appsettings ou variável `ConnectionStrings__SeiMonitor`) — connection string completa.
2. Se não houver connection string completa, monta-se a partir de variáveis isoladas:
   - Senha: `Database:PasswordEnvironmentVariable` → `SEI_DB_PASSWORD` → `SEI_MONITOR_DB_PASSWORD` → `PGPASSWORD`.
   - Se não houver senha explícita, usa-se `PGPASSFILE` (arquivo de senhas do PostgreSQL) ou o padrão do sistema (`%APPDATA%\\postgresql\\pgpass.conf` no Windows, `~/.pgpass` no Unix). A aplicação passa apenas o caminho ao Npgsql; nunca lê o conteúdo do arquivo.
   - Host: `SEI_DB_HOST` ou `PGHOST` (padrão `localhost`).
   - Banco: `SEI_DB_NAME` ou `PGDATABASE` (padrão `sei_monitor`).
   - Usuário: `SEI_DB_USER` ou `PGUSER` (padrão `sei_monitor_app`).
   - Porta: `SEI_DB_PORT` ou `PGPORT` (padrão 5432).
   - SSL: `SEI_DB_SSL_MODE` ou `Database:SslMode`.
3. `Database:CommandTimeoutSeconds` — timeout de comandos SQL, clamp entre 5 e 120 s (padrão 30).

O `DbContext` é configurado com `EnableRetryOnFailure` (3 tentativas, 5 s de atraso) para tolerar falhas transitórias do PostgreSQL.

### Migrations

- Não há aplicação automática de schema no startup (sem `Database.Migrate()`).
- A migration está em `src/InfraStructure/Migrations/20261125082055_InitialCreate.cs`.
- Aplica-se manualmente com:
  ```powershell
  dotnet ef database update `
    --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `
    --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj
  ```
- Pode-se gerar um script idempotente para bancos gerenciados:
  ```powershell
  dotnet ef migrations script --idempotent `
    --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `
    --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj `
    --output migrate.sql
  ```

## Entidades/tabelas confirmadas

O schema consiste em **quatro tabelas**, definidas pela migration `InitialCreate`:

| Tabela | Descrição |
|---|---|
| `processos` | Registro de um processo monitorado (raiz do agregado). |
| `consultas` | Snapshots de consultas realizadas a um processo. |
| `protocolos` | Protocolos/documentos públicos associados a um processo. |
| `movimentacoes` | Andamentos/histórico de tramitação de um processo. |

### Tabela `processos`

| Campo | Tipo | Nulo | Descrição |
|---|---|---|---|
| `Id` | `bigint` | Não | PK, identidade (IdentityByDefaultColumn). |
| `Numero` | `varchar(100)` | Não | Número do processo, índice único `ux_processos_numero`. |
| `Tipo` | `varchar(100)` | Sim | Tipo do processo. |
| `DataGeracao` | `varchar(50)` | Sim | Data de geração do processo. |
| `Interessados` | `varchar(500)` | Sim | Interessados no processo. |
| `AcessoRestrito` | `boolean` | Não | Indica se o processo possui restrição de acesso. |
| `PrimeiraConsultaUtc` | `timestamp with time zone` | Não | Carimbo da primeira consulta. |
| `UltimaConsultaUtc` | `timestamp with time zone` | Não | Carimbo da última consulta. |
| `UnidadeAtual` | `varchar(200)` | Sim | Unidade atual do processo. |

### Tabela `consultas`

| Campo | Tipo | Nulo | Descrição |
|---|---|---|---|
| `Id` | `bigint` | Não | PK, identidade. |
| `ProcessoId` | `bigint` | Não | FK para `processos.Id` (cascade). |
| `ConsultadoEmUtc` | `timestamp with time zone` | Não | Data/hora da consulta. |
| `Fonte` | `varchar(1000)` | Não | URL fonte da consulta. |
| `ProtocolosDeclarados` | `integer` | Sim | Quantidade de protocolos declarada na página. |
| `AndamentosDeclarados` | `integer` | Sim | Quantidade de andamentos declarada na página. |
| `QuantidadeProtocolos` | `integer` | Não | Quantidade efetiva de protocolos extraídos. |
| `QuantidadeMovimentacoes` | `integer` | Não | Quantidade efetiva de movimentações extraídas. |
| `UnidadeAtual` | `varchar(200)` | Sim | Unidade atual no momento da consulta. |

Índices: `ix_consultas_processo` (ProcessoId), `ix_consultas_processo_data` (ProcessoId, ConsultadoEmUtc).

### Tabela `protocolos`

| Campo | Tipo | Nulo | Descrição |
|---|---|---|---|
| `Id` | `bigint` | Não | PK, identidade. |
| `ProcessoId` | `bigint` | Não | FK para `processos.Id` (cascade). |
| `Chave` | `varchar(64)` | Não | Hash SHA-256 de campos normalizados do protocolo. |
| `Numero` | `varchar(100)` | Não | Número do protocolo/documento. |
| `Natureza` | `varchar(50)` | Não | Natureza (ex.: "Processo", "Documento"). |
| `Tipo` | `varchar(100)` | Sim | Tipo do protocolo. |
| `Data` | `varchar(50)` | Sim | Data do protocolo. |
| `DataInclusao` | `varchar(50)` | Sim | Data de inclusão. |
| `Unidade` | `varchar(200)` | Sim | Unidade. |
| `AcessoRestrito` | `boolean` | Não | Indica restrição de acesso. |
| `PrimeiraDeteccaoUtc` | `timestamp with time zone` | Não | Primeira vez que foi detectado. |
| `UltimaDeteccaoUtc` | `timestamp with time zone` | Não | Última vez que foi detectado. |

Índices: `ix_protocolos_processo` (ProcessoId), `ux_protocolos_processo_chave` (ProcessoId, Chave, único).

### Tabela `movimentacoes`

| Campo | Tipo | Nulo | Descrição |
|---|---|---|---|
| `Id` | `bigint` | Não | PK, identidade. |
| `ProcessoId` | `bigint` | Não | FK para `processos.Id` (cascade). |
| `ConsultaId` | `bigint` | Não | FK para `consultas.Id` (cascade). |

## Relacionamentos

```mermaid
erDiagram
    PROCESSOS ||--o{ CONSULTAS : "possui"
    PROCESSOS ||--o{ PROTOCOLOS : "possui"
    PROCESSOS ||--o{ MOVIMENTACOES : "possui"
    CONSULTAS ||--|{ MOVIMENTACOES : "gera"

    PROCESSOS {
        bigint Id PK
        varchar(100) Numero UK
        varchar(100) Tipo
        varchar(50) DataGeracao
        varchar(500) Interessados
        boolean AcessoRestrito
        timestampz PrimeiraConsultaUtc
        timestampz UltimaConsultaUtc
        varchar(200) UnidadeAtual
    }

    CONSULTAS {
        bigint Id PK
        bigint ProcessoId FK
        timestampz ConsultadoEmUtc
        varchar(1000) Fonte
        integer? ProtocolosDeclarados
        integer? AndamentosDeclarados
        integer QuantidadeProtocolos
        integer QuantidadeMovimentacoes
        varchar(200) UnidadeAtual
    }

    PROTOCOLOS {
        bigint Id PK
        bigint ProcessoId FK
        varchar(64) Chave
        varchar(100) Numero
        varchar(50) Natureza
        varchar(100) Tipo

## Índices e restrições

| Índice | Tabela | Colunas | Único | Nome no banco |
|---|---|---|---|---|
| PK | `processos` | `Id` | Sim | `PK_processos` |
| UK | `processos` | `Numero` | Sim | `ux_processos_numero` |
| IX | `consultas` | `ProcessoId` | Não | `ix_consultas_processo` |
| IX | `consultas` | `ProcessoId, ConsultadoEmUtc` | Não | `ix_consultas_processo_data` |
| PK | `protocolos` | `Id` | Sim | `PK_protocolos` |
| IX | `protocolos` | `ProcessoId` | Não | `ix_protocolos_processo` |
| UK | `protocolos` | `ProcessoId, Chave` | Sim | `ux_protocolos_processo_chave` |
| PK | `movimentacoes` | `Id` | Sim | `PK_movimentacoes` |
| IX | `movimentacoes` | `ProcessoId` | Não | `ix_movimentacoes_processo` |
| IX | `movimentacoes` | `ProcessoId, DataHora` | Não | `ix_movimentacoes_processo_data` |
| UK | `movimentacoes` | `ProcessoId, Chave` | Sim | `ux_movimentacoes_processo_chave` |

A deduplicação é **escopada por processo**: as chaves únicas de protocolos e movimentações incluem `ProcessoId`, permitindo que a mesma chave SHA-256 exista em processos diferentes.

## Migrations e procedimento de atualização

Apenas uma migration existe atualmente: `20261125082055_InitialCreate`, que cria as quatro tabelas e seus índices.

Procedimento controlado:

1. Faça backup do banco antes de qualquer migration.
2. Gere o script SQL (recomendado para bancos gerenciados):
   ```powershell
   dotnet ef migrations script --idempotent `

## Estratégia de persistência

- O `DbContext` (`SeiRjMonitorDbContext`) implementa `IUnitOfWork` com `SaveChangesAsync`.
- Repositórios são registrados como **scoped** no contêiner DI.
- Cada chamada de ferramenta MCP (`SeiTools`) cria seu próprio escopo de DI para isolar o `DbContext`.
- O `MonitoramentoBackgroundService` também cria escopo por operação, pois é um singleton hospedado.
- `AsSplitQuery()` é usado ao carregar o agregado `Processo` com suas três coleções (`Consultas`, `Protocolos`, `Movimentacoes`) para evitar produto cartesiano.
- `SaveChangesAsync` é chamado uma vez por consulta, garantindo atomicidade da gravação de snapshot + protocolos + movimentações.
- O `DataHora` de movimentações é mapeado como `timestamp without time zone` porque o SEI-RJ exibe data/hora local sem fuso; usar `timestamp with time zone` faria o Npgsql rejeitar a escrita com `DateTimeKind.Unspecified`.

## Snapshots, histórico e deduplicação

- Cada consulta grava um registro em `consultas` (snapshot), mesmo que não haja novidades.
- Protocolos e movimentações são upserts lógicos: se a chave SHA-256 já existe para o processo, apenas atualiza `UltimaDeteccaoUtc`; caso contrário, insere novo registro.
- Na primeira consulta de um processo, nada é marcado como novo (linha de base).
- O histórico completo é mantido: todas as consultas e todas as movimentações detectadas permanecem no banco.
- Não há arquivamento automático nem exclusão de dados antigos.

## Como inspecionar o banco localmente

Exemplos de consultas somente leitura (placeholders, sem credenciais reais):

```sql
-- Processos monitorados e última consulta
SELECT p."Numero", p."UltimaConsultaUtc", COUNT(c."Id") AS consultas
FROM processos p
LEFT JOIN consultas c ON c."ProcessoId" = p."Id"
GROUP BY p."Id", p."Numero", p."UltimaConsultaUtc"
ORDER BY p."UltimaConsultaUtc" DESC;

-- Tamanho das tabelas
SELECT relname, pg_size_pretty(pg_total_relation_size(relid)) AS tamanho
FROM pg_catalog.pg_stat_user_tables
ORDER BY pg_total_relation_size(relid) DESC;

-- Movimentações detectadas nas últimas 24h
SELECT COUNT(*) FROM movimentacoes
WHERE "DetectadaEmUtc" > now() - interval '24 hours';

-- Protocolos por processo
SELECT p."Numero", COUNT(pr."Id") AS total_protocolos
FROM processos p
LEFT JOIN protocolos pr ON pr."ProcessoId" = p."Id"
GROUP BY p."Id", p."Numero";
```

Para conectar via `psql`, use a connection string configurada (ex.: variável `ConnectionStrings__SeiMonitor`) ou variáveis `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` e `PGPASSWORD`/`PGPASSFILE`. Não inclua senhas reais nos comandos.

Backup e restauração (Docker Compose):

```bash
# Backup
docker compose exec -T postgres pg_dump -U "${POSTGRES_USER:-sei_monitor_app}" \
  -d "${POSTGRES_DB:-sei_monitor}" -Fc > backup.dump

# Restauração em banco vazio
docker compose exec -T postgres pg_restore -U "${POSTGRES_USER:-sei_monitor_app}" \
  -d "${POSTGRES_DB:-sei_monitor}" --clean --if-exists < backup.dump
```

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — camadas e componente de persistência.
- [Fluxo de execução](03-fluxo-de-execucao.md) — persistência e comparação no fluxo de consulta.
- [Configuração e deploy](09-configuracao-e-deploy.md) — variáveis de banco, migrations e deploy.
- [Operações preexistentes](operations.md) — consultas úteis, backup e restauração.
- [Auditoria preexistente](audit-report.md) — validação do schema e migrations.
     --project src/InfraStructure/SeiRjMonitor.Infrastructure.csproj `
     --startup-project src/Presentation/SeiRjMonitor.Presentation.csproj `
     --output migrate.sql
   ```
3. Revise o SQL e aplique com um usuário de migração (separado do usuário da aplicação):
   ```bash
   psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f migrate.sql
   ```
4. Não edite migrations já aplicadas; crie uma nova se necessário.
5. O usuário da aplicação não precisa de permissão para alterar schema em runtime.
        varchar(50) Data
        varchar(50) DataInclusao
        varchar(200) Unidade
        boolean AcessoRestrito
        timestampz PrimeiraDeteccaoUtc
        timestampz UltimaDeteccaoUtc
    }

    MOVIMENTACOES {
        bigint Id PK
        bigint ProcessoId FK
        bigint ConsultaId FK
        varchar(64) Chave
        timestamp DataHora
        varchar(200) Unidade
        varchar(200) UnidadeRelacionada
        varchar(100) TipoEvento
        varchar(2000) Descricao
        timestampz DetectadaEmUtc
    }
```

- Todas as FKs usam `onDelete: ReferentialAction.Cascade` a partir de `processos`.
- `Consulta` possui `ProcessoId` e navegação para `Processo`.
- `MovimentacaoItem` possui `ProcessoId` e `ConsultaId`.
| `Chave` | `varchar(64)` | Não | Hash SHA-256 de campos normalizados da movimentação. |
| `DataHora` | `timestamp without time zone` | Não | Data/hora da movimentação (sem fuso, pois o SEI exibe local). |
| `Unidade` | `varchar(200)` | Não | Unidade que registrou a movimentação. |
| `UnidadeRelacionada` | `varchar(200)` | Sim | Unidade relacionada (se houver). |
| `TipoEvento` | `varchar(100)` | Não | Tipo classificado (ex.: Recebimento, Conclusão, Remessa, Outro). |
| `Descricao` | `varchar(2000)` | Não | Descrição completa da movimentação. |
| `DetectadaEmUtc` | `timestamp with time zone` | Não | Carimbo de quando foi detectada pelo monitor. |

Índices: `ix_movimentacoes_processo` (ProcessoId), `ix_movimentacoes_processo_data` (ProcessoId, DataHora), `ux_movimentacoes_processo_chave` (ProcessoId, Chave, único).