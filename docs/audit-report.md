# Relatório final de auditoria — SEI-RJ Monitor MCP

Data: 26/11/2026 · Branch: `development` · Base: `36e59c2`

## 1. Sumário executivo

O repositório foi auditado de ponta a ponta e as correções seguras foram implementadas.
O build Release está limpo (0 avisos, 0 erros) e a suíte passou de 20 para **107 testes**,
todos verdes, sem dependência de PostgreSQL, Docker ou acesso real ao SEI.

Os quatro contratos MCP foram preservados e validados por smoke test via Streamable HTTP:
`consultar_processo`, `historico_processo`, `listar_processos_monitorados` e
`validar_link_processo`.

Principais riscos corrigidos: deduplicação com escopo global (poderia suprimir dados
legítimos), ausência de autenticação no `/mcp`, SSRF apenas parcialmente mitigado,
dependências com vulnerabilidades conhecidas, ausência de health checks, falha do host
por exceções do worker e configuração de produção com senha placeholder versionada.

**Veredito: READY WITH WARNINGS.** O código está pronto para deploy controlado. Ficam
pendentes itens operacionais e uma validação de imagem Docker que não pôde ser executada
neste ambiente (daemon indisponível).

## 2. Auditoria por fase

### Fase 1 — Arquitetura e DDD

- Confirmada a separação Domain/Application/Infrastructure/Presentation.
- Corrigida a violação de dependência **Application → Infrastructure** com a introdução
  das portas `ISeiPageLoader` e `ISeiPageParser` em `src/Application/Abstractions`.
- `SeiPageParser` passou a ser instância injetável, mantendo o comportamento do parser.
- Mantidos agregado `Processo`, value object `SeiLink` e interfaces de repositório.
- Nenhum serviço desnecessário foi introduzido (sem RabbitMQ, Outbox, Redis ou
  Kubernetes).

### Fase 2 — Segurança

- **Autenticação MCP**: middleware com `Authorization: Bearer` ou `X-Api-Key`,
  comparação SHA-256 em tempo constante, obrigatória em Production e validada no startup.
- **SSRF**: cadeia completa — allowlist de esquema/host/porta/caminho, revalidação de
  cada redirecionamento, bloqueio de IPs privados/reservados na resolução DNS e no
  `ConnectCallback` (anti-rebinding), proxy automático desabilitado.
- **Limites**: timeout total incluindo corpo, timeout de conexão, limite de 5 MB
  verificado por `Content-Length` e durante a leitura, máximo de redirecionamentos.
- **Erros**: mensagens genéricas ao cliente; detalhes apenas no log.
- **Cabeçalhos**: CSP, nosniff, frame-deny, referrer-policy, permissions-policy,
  cache-control e HSTS em Production.
- **Segredos**: nenhuma credencial real versionada; connection string removida do
  `appsettings.json` base; `.env.example` e `.gitignore` cobrem o fluxo local.
- **Dependências**: `Npgsql` e `Microsoft.Extensions.Caching.Memory` vulneráveis foram
  atualizados; varredura final sem vulnerabilidades.

### Fase 3 — PostgreSQL, persistência e migrations

- Schema revisado: FKs em cascata, `ux_processos_numero` e índices únicos
  `(processo_id, chave)` para protocolos e movimentações.
- Corrigida a deduplicação para escopo por `ProcessoId` nas interfaces e repositórios.
- Mantidas migrations manuais (sem `Database.Migrate()` no startup).
- Script idempotente gerado e validado: 153 linhas, cria as quatro tabelas e o histórico
  de migrations.
- Retry transitório (3 tentativas, 5 s) e timeout de comando configurável.
- Backup/restauração documentados com `pg_dump`/`pg_restore`.

### Fase 4 — BackgroundService

- Reescrito com `MonitoramentoOptions` (intervalo, pausa, espera inicial e timeout de
  ciclo).
- `SemaphoreSlim` impede sobreposição de ciclos.
- Timeout por ciclo com `CancellationTokenSource` vinculado ao shutdown.
- Falha por processo e falha do ciclo são isoladas; o host não cai.
- Escopo de DI por operação, adequado ao serviço singleton.
- Primeiro ciclo após `StartupDelaySeconds`; monitoramento desativável com intervalo 0.

### Fase 5 — Logs e observabilidade

- Logs estruturados por evento: startup, ciclo, processo, timeout, autenticação e banco.
- Nenhuma senha, chave ou connection string é logada.
- Descrições de movimentações são truncadas em 200 caracteres.
- Resumo do último ciclo exposto em `GET /`.

### Fase 6 — Health checks

- `/health/live`: sem dependências (liveness).
- `/health/ready`: PostgreSQL com timeout de 5 s, sem detalhes sensíveis.
- Respostas JSON mínimas; 200/503 conforme estado.
- Smoke test confirmou 200 em live e 503 em ready com o banco indisponível.

### Fase 7 — Testes

- 107 testes, 0 falhas: domínio/URLs, SSRF, loader HTTP, parser, autenticação,
  persistência (InMemory e SQLite para constraints), aplicação, ferramentas MCP
  (erro estruturado de infraestrutura e cancelamento) e ciclo de monitoramento
  (falha isolada, timeout, cancelamento, não sobreposição e banco indisponível).
- Fixtures locais; nenhum teste acessa o SEI real ou exige PostgreSQL.

### Fase 8 — Docker/Compose e preparação de deploy

- `Dockerfile` multi-stage validado por revisão, com usuário não-root, `curl` apenas para
  health check e imagem de runtime ASP.NET 8.
- `docker-compose.yml` sem senha hardcoded, com validação obrigatória de variáveis,
  PostgreSQL restrito ao loopback, health checks e `no-new-privileges`.
- `.dockerignore` e `.env.example` adicionados.
- `docker compose config` validado com sucesso.
- Build da imagem **não executado**: daemon Docker indisponível
  (`npipe:////./pipe/dockerDesktopLinuxEngine`).
- CI adicionado em `.github/workflows/ci.yml` (build, testes, dependências vulneráveis
  e build da imagem).

### Fase 9 — Configuração de produção

- `appsettings.json` sem segredos e com defaults operacionais.
- `appsettings.Development.json` sem senha: usa variáveis de ambiente ou o pgpass.
- `appsettings.Production.json` com autenticação obrigatória e níveis de log adequados.
- Resolução da conexão em cascata: `ConnectionStrings:SeiMonitor` → senha em
  `Database:PasswordEnvironmentVariable`/`SEI_DB_PASSWORD`/`SEI_MONITOR_DB_PASSWORD`/
  `PGPASSWORD` → arquivo `PGPASSFILE` (ou pgpass padrão do sistema). Nenhuma senha é
  copiada para arquivo de configuração nem aparece em log.
- Validação de startup para connection string, chave ausente/curta e valor `change_me`.
- Warning explícito quando `AllowedHosts` está aberto em Production.

### Fase 10 — Documentação

- `README.md` reescrito (visão geral, execução local, Docker, configuração, ferramentas,
  cliente MCP, testes e limitações).
- `docs/architecture.md`: camadas, modelo, fluxos, decisões e escalabilidade.
- `docs/security.md`: modelo de ameaça, mitigações, riscos residuais e checklist.
- `docs/deployment.md`: Compose, Render, Railway, migrations controladas e rollback.
- `docs/operations.md`: health, logs, backup, rotação de chave, runbook e manutenção.

### Fase 11 — Validação final

| Verificação | Resultado |
| --- | --- |
| `dotnet build -c Release` | 0 avisos, 0 erros |
| `dotnet test -c Release` | 107/107 aprovados |
| Pacotes vulneráveis | Nenhum (6 projetos) |
| Script de migration idempotente | Gerado (153 linhas) |
| `docker compose config` | Válido |
| `/health/live` | 200 |
| `/health/ready` sem banco | 503 (esperado) |
| `/mcp` sem chave em Production | 401 |
| `/mcp` com chave em Production | 200 |
| `tools/list` | Exatamente 4 ferramentas |
| Build da imagem Docker | Não executado (daemon indisponível) |
| MCP Inspector interativo | Não executado; substituído pelo smoke test via script |
| Migrations aplicadas em banco real | Não executado (sem credenciais) |

## 3. Segurança — achados e tratamento

| # | Achado | Severidade | Status |
| --- | --- | --- | --- |
| 1 | Endpoint MCP sem autenticação | Alta | Corrigido |
| 2 | SSRF sem validação de IP/DNS e redirecionamentos | Alta | Corrigido |
| 3 | `Npgsql 8.0.0` e cache transitivo vulneráveis | Alta | Corrigido |
| 4 | Deduplicação global entre processos | Alta | Corrigido |
| 5 | Senha placeholder na configuração base | Média | Corrigido |
| 6 | Exceções não tratadas podiam derrubar o host | Média | Corrigido |
| 7 | Sem health checks nem limites de request | Média | Corrigido |
| 8 | Falta de rate limit na aplicação | Média | Pendente (proxy) |
| 9 | `AllowedHosts=*` por padrão | Baixa | Mitigado com warning; configurar no deploy |
| 10 | Chave única sem rotação automática | Média | Procedimento documentado |
| 11 | TLS terminado fora da aplicação | Baixa | Documentado |
| 12 | Layout HTML do SEI pode mudar | Média | Falha isolada + fixture/teste |
| 13 | Senha do banco em arquivo de configuração | Média | Corrigido: variáveis de ambiente ou `PGPASSFILE` (somente o caminho é passado ao Npgsql) |
| 14 | Falha de banco escapava como exceção da ferramenta MCP | Baixa | Corrigido: resposta JSON `FalhaInfraestrutura`, sem detalhes sensíveis |

## 4. Arquitetura — mudanças relevantes

- Application deixou de referenciar Infrastructure (portas de rede/parse).
- Opções normalizadas e validadas (`MonitoramentoOptions`, `SeiHttpOptions`).
- Loader HTTP com handler seguro e testável.
- `SeiPageParser` como serviço de instância.
- Removida a aplicação automática de migrations; deploy controlado.
- Mantida uma única instância de worker por decisão de simplicidade (sem lock
  distribuído).

## 5. Testes

```
Aprovado: 107 · Falha: 0 · Ignorado: 0
```

Cobertura funcional por área:

- Domínio: links válidos/inválidos, subdomínios, portas, userinfo, formatos de parâmetro.
- Segurança: 21 casos de IPs bloqueados/permitidos, redirecionamentos, tamanho, timeout,
  cancelamento, sanitização de erros e autenticação.
- Persistência: agregado, dedup por processo, constraint única, projeções e agendamento.
- Aplicação: linha de base, novidades, idempotência, listagem e histórico.
- Monitoramento: sucesso, falha isolada, timeout, cancelamento, sobreposição e banco
  indisponível.

## 6. Deploy

Pronto para uso:

- Imagem multi-stage e Compose com variáveis obrigatórias.
- Health checks de liveness/readiness.
- Migrations manuais com script idempotente.
- Configuração de produção sem segredos.
- Documentação de Render, Railway e VM.

Pendências antes de tráfego real:

1. Buildar a imagem em ambiente com Docker ativo (ou deixar o CI executar).
2. Aplicar a migration `20261125082055_InitialCreate` no banco de produção.
3. Definir `ConnectionStrings__SeiMonitor`, `Seguranca__ApiKey` e `AllowedHosts`.
4. Terminar TLS e configurar rate limit no proxy.
5. Executar o smoke test com um link público real do SEI.
6. Configurar backup e alerta de readiness.

## 7. Checklist final

### Código e arquitetura

- [x] Build Release sem avisos ou erros
- [x] 107 testes aprovados
- [x] Camadas e regra de dependência preservadas
- [x] Contratos das 4 ferramentas MCP mantidos
- [x] Migrations sem operações destrutivas
- [x] Deduplicação escopada por processo

### Segurança

- [x] Autenticação obrigatória no `/mcp` em Production
- [x] Comparação de chave em tempo constante
- [x] Allowlist de host/esquema/porta/caminho
- [x] Revalidação de redirecionamentos
- [x] Bloqueio de IPs privados na conexão (anti-rebinding)
- [x] Limites de timeout e tamanho
- [x] Erros genéricos ao cliente
- [x] Cabeçalhos de segurança
- [x] Nenhum segredo versionado
- [x] Dependências sem vulnerabilidades conhecidas
- [ ] Rate limit no proxy (operacional)
- [ ] `AllowedHosts` restrito ao domínio real (operacional)
- [ ] HTTPS obrigatório na borda (operacional)

### Persistência e operação

- [x] Schema e índices revisados
- [x] Script de migration idempotente gerado
- [x] Retry e timeout de comando configurados
- [x] Health checks live/ready
- [x] Worker resiliente e sem sobreposição
- [x] Backup/restauração documentados
- [ ] Migration aplicada no banco de produção
- [ ] Backup automático e alerta configurados

### Deploy

- [x] Dockerfile multi-stage e não-root
- [x] Compose sem senha hardcoded
- [x] Health checks no Compose
- [x] `.env.example` e `.dockerignore`
- [x] `docker compose config` válido
- [x] CI de build, testes, vulnerabilidades e imagem
- [!] Build da imagem Docker não validado neste ambiente (daemon indisponível) — validar no CI ou em host com Docker
- [ ] MCP Inspector interativo (substituído por smoke test automatizado)
- [ ] Smoke test com link real do SEI em produção

### Documentação

- [x] `README.md`
- [x] `docs/architecture.md`
- [x] `docs/security.md`
- [x] `docs/deployment.md`
- [x] `docs/operations.md`

## 8. Veredito

**READY WITH WARNINGS**

O código e a configuração estão prontos para um primeiro deploy controlado. Não há
bloqueadores de código, dados ou segurança conhecidos. O único item marcado como
bloqueador (`[!]`) é a validação da imagem Docker, impossível neste ambiente por falta
do daemon; o workflow de CI executa essa etapa. As demais pendências são passos
operacionais deliberadamente não executados (migrations, segredos reais, TLS, rate limit
e smoke test no SEI de produção).
