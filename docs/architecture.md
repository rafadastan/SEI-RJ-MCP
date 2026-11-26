# Arquitetura

## Visão geral

O SEI-RJ Monitor é um servidor MCP stateless do ponto de vista do protocolo, com estado
durável apenas no PostgreSQL. A aplicação lê a página pública de consulta processual do
SEI-RJ, normaliza os dados, persiste snapshots e expõe consultas locais por ferramentas
MCP.

```
Cliente MCP (Inspector, IDE, ChatGPT)
        │  Streamable HTTP + API key
        ▼
Presentation (ASP.NET Core)
  ├─ SeiTools (4 ferramentas MCP)
  ├─ ApiKeyAuthenticationMiddleware
  ├─ MonitoramentoBackgroundService
  └─ Health checks (/health/live, /health/ready)
        │
        ▼
CrossCutting (composição de DI)
        │
        ├──────────────► Application
        │                  ├─ SeiConsultaService
        │                  ├─ SeiMonitoramentoService
        │                  └─ portas: ISeiPageLoader, ISeiPageParser
        │                        ▲
        │                        │ implementadas por
        └──────────────► Infrastructure
                           ├─ SeiPageLoader (HTTP + SSRF)
                           ├─ SeiPageParser (AngleSharp)
                           ├─ SegurancaRede (DNS/IP)
                           └─ EF Core + Npgsql
                                 │
                                 ▼
                            PostgreSQL
```

## Camadas e regras de dependência

| Camada | Responsabilidade | Depende de |
| --- | --- | --- |
| `Domain` | Agregado `Processo`, entidades, value objects (`SeiLink`), regras e interfaces de repositório | Nada |
| `Application` | Casos de uso, orquestração, DTOs, portas de rede/parse e opções | `Domain` |
| `Infrastructure` | EF Core/Npgsql, repositórios, HTTP, parser, migrations | `Domain`, `Application` |
| `CrossCutting` | Registro de DI e opções | `Application`, `Infrastructure` |
| `Presentation` | Host ASP.NET Core, MCP, autenticação, background service, health | `CrossCutting` |
| `Tests` | xUnit e fixtures | `Domain`, `Application`, `Infrastructure`, `Presentation` |

Regra aplicada nesta revisão: **Application não referencia Infrastructure**. O acesso à
rede e o parse são definidos por portas (`ISeiPageLoader`, `ISeiPageParser`) em
`Application/Abstractions` e implementados na Infrastructure. Isso permite testar os
casos de uso com dublês, sem HTTP real.

## Modelo de domínio

- `Processo` (raiz de agregado): número, tipo, data de geração, interessados, restrição
  de acesso, unidade atual e carimbos de primeira/última consulta.
- `Consulta`: snapshot de uma leitura (fonte, data, contagens declaradas e extraídas).
- `ProtocoloItem`: protocolo/documento público associado ao processo, com chave de
  deduplicação.
- `MovimentacaoItem`: andamento do histórico com chave de deduplicação.
- `SeiLink`: value object que valida e classifica o link público.

### Persistência

```
processos (Id, Numero UNIQUE, ...)
  ├─ consultas (ProcessoId FK, ConsultadoEmUtc, Fonte, ...)
  ├─ protocolos (ProcessoId FK, Chave, ...)      UNIQUE(ProcessoId, Chave)
  └─ movimentacoes (ProcessoId FK, ConsultaId FK, Chave, ...)
                                                 UNIQUE(ProcessoId, Chave)
```

- FKs em cascata a partir de `processos`.
- `consultas` guarda o histórico de leituras; `protocolos` e `movimentacoes` são
  upserts lógicos deduplicados por `SHA-256` de campos normalizados.
- A deduplicação é **escopada por processo**. A revisão corrigiu a consulta que antes
  verificava a chave globalmente e podia suprimir um item legítimo de outro processo.

### Consultas

- `ObterPorNumeroAsync` carrega o agregado com `AsSplitQuery()` para evitar produto
  cartesiano entre as três coleções.
- `ListarParaAgendamentoAsync` usa a consulta mais recente de cada processo (janela
  `GROUP BY` + `First`) para reutilizar o link mais novo.
- `ListarResumosAsync` projeta contagens diretamente no banco.

## Fluxos

### Consulta manual (`consultar_processo`)

1. `SeiLink.TryParse` valida HTTPS, host `sei.rj.gov.br`/subdomínio, porta 443, ausência
   de credenciais na URL e caminho exato `md_pesq_processo_exibir.php`.
2. `SeiPageLoader` baixa o HTML com timeout, limite de bytes e revalidação de cada
   redirecionamento. A resolução DNS é validada no `ConnectCallback`, impedindo
   *DNS rebinding* para IPs privados.
3. `SeiPageParser` extrai `#tblCabecalho`, `#tblDocumentos` e `#tblHistorico` usando
   leitura posicional (o SEI entrega `data-label` rotacionado).
4. `SeiMonitoramentoService` cria/atualiza o processo, grava a consulta, faz upsert de
   protocolos e movimentações e calcula as novidades.
5. A resposta MCP inclui metadados, protocolos, histórico e o bloco `monitoramento`
   (`primeiraConsulta`, `protocolosNovos`, `movimentacoesNovas`, etc.).

### Ciclo agendado

`MonitoramentoBackgroundService`:

- Respeita `StartupDelaySeconds` antes do primeiro ciclo.
- Usa `PeriodicTimer` e um `SemaphoreSlim` para garantir que um ciclo nunca se sobrepõe
  ao anterior.
- Aplica `CycleTimeoutSeconds` por ciclo e isola falhas: erro em um processo é logado e
  o ciclo continua; erro no ciclo inteiro é logado e o host continua.
- Consulta os processos do mais antigo para o mais novo, com pausa configurável entre
  eles, reduzindo carga sobre o SEI.
- Cada operação abre um escopo de DI próprio, pois o host é singleton e os repositórios
  são scoped.

## Decisões arquiteturais

| Decisão | Motivo | Consequência |
| --- | --- | --- |
| Portas de rede/parse na Application | Remover dependência Application → Infrastructure e permitir testes | Mais duas interfaces pequenas |
| Migrations manuais | Evitar alteração automática de schema em produção | Um passo explícito no deploy |
| Sem Outbox/RabbitMQ/Redis | Simplicidade operacional e custo | Sem notificações externas; monitoramento consulta o banco |
| Dedup por `(ProcessoId, Chave)` | Correção de escopo e integridade | Índices únicos por processo |
| Retry do Npgsql (3×, 5 s) | Tolerar falhas transitórias de rede/banco | Requer operações idempotentes; constraints protegem duplicidade |
| Split query no agregado | Evitar explosão cartesiana | Três consultas por leitura de processo |
| API key comparada por hash em tempo constante | Evitar vazamento por timing | Chave única por ambiente |
| Health check readiness com timeout curto | Não pendurar o orquestrador | Pode retornar 503 durante indisponibilidade do banco |

## Escalabilidade e limites

- O ciclo é sequencial na instância. Duas réplicas com o mesmo banco fariam consultas
  duplicadas ao SEI; para escalar horizontalmente, seria necessário um lock distribuído
  (não implementado por decisão de simplicidade).
- O histórico completo do processo é carregado em memória a cada consulta. Para
  processos com milhares de movimentações, o custo cresce; a paginação do histórico é
  uma evolução possível.
- O parser depende do layout HTML atual do SEI. Mudanças de layout geram
  `LayoutDesconhecido`, tratado como falha isolada e sinalizado na resposta.
- O servidor não faz TLS; TLS é responsabilidade do proxy/plataforma.

## Estrutura de diretórios

```
src/Domain/           Aggregates, Interfaces, SeiLink, SeiTextos, ClassificadorEvento
src/Application/      SeiConsultaService, SeiMonitoramentoService, Abstractions, Persistence
src/InfraStructure/   Persistence (DbContext, configurations, migrations), Repositories, Services
src/CrossCutting/     Ioc/DependencyInjection
src/Presentation/     Program, SeiTools, middleware, health check, background service
src/Test/XUnit/       DomainTests, SecurityTests, ParserTests, PersistenceTests,
                      ApplicationTests, MonitoringTests, Fixtures
```
