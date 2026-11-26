# Arquitetura — SEI-RJ Monitor MCP

## Visão arquitetural

O SEI-RJ Monitor é um servidor **MCP stateless do ponto de vista do protocolo**, com estado durável apenas no **PostgreSQL**. A aplicação lê a página pública de consulta processual do SEI-RJ, normaliza os dados, persiste snapshots e expõe consultas locais por ferramentas MCP.

A arquitetura segue uma separação em camadas inspirada em **DDD (Domain-Driven Design) simplificado**:

- **Domain**: agregado `Processo`, entidades, value objects, regras de negócio e interfaces de repositório. Não depende de nada.
- **Application**: casos de uso, orquestração, DTOs/records de leitura e portas de saída (interfaces de rede e parse). Depende apenas de `Domain`.
- **Infrastructure**: EF Core/Npgsql, repositórios, HTTP seguro, parser AngleSharp, migrations. Depende de `Domain` e `Application`.
- **CrossCutting**: composição de injeção de dependência (DI) e opções. Depende de `Application` e `Infrastructure`.
- **Presentation**: host ASP.NET Core, ferramentas MCP, autenticação, background service, health checks. Depende de `CrossCutting`.
- **Tests**: xUnit e fixtures. Referencia os projetos de produção para testes.

Regra de dependência aplicada: **Application não referencia Infrastructure**. O acesso à rede e o parse são definidos por portas (`ISeiPageLoader`, `ISeiPageParser`) em `Application/Abstractions` e implementados na `Infrastructure`. Isso permite testar os casos de uso com dublês, sem HTTP real.

## Diagrama de arquitetura

```mermaid
graph TD
    subgraph Cliente["Cliente MCP (Inspector, IDE, ChatGPT, LLM)"]
        C1[Streamable HTTP + API key]
    end

    subgraph Pres["Presentation — ASP.NET Core"]
        P1[SeiTools — 4 ferramentas MCP]
        P2[MonitoramentoBackgroundService — ciclo periódico]
        P3[ApiKeyAuthenticationMiddleware — /mcp]
        P4[Health checks /health/live, /health/ready]
        P5[Program.cs — host, Kestrel, CORS, headers]
    end

    subgraph CC["CrossCutting — DI e opções"]
        D1[DependencyInjection — registro de serviços]
    end

    subgraph App["Application — casos de uso"]
        A1[SeiConsultaService — valida link, carrega página, parse]
        A2[SeiMonitoramentoService — persistência, comparação, novidades]
        A3[MonitoramentoOptions — configuração do ciclo]
        A4[Abstrações: ISeiPageLoader, ISeiPageParser]
        A5[RegistrosPersistencia — DTOs/records de leitura]
    end

    subgraph Infra["InfraStructure — infraestrutura"]
        I1[SeiPageLoader — HTTP + anti-SSRF]
        I2[SeiPageParser — AngleSharp extração]
        I3[SegurancaRede — DNS/IP público permitido]
        I4[SeiRjMonitorDbContext — EF Core + Npgsql]
        I5[Repositórios EF Core — Processo, Consulta, Protocolo, Movimentacao]
        I6[Migrations — 20261125082055_InitialCreate]
    end

    subgraph DB[(PostgreSQL)]
        DB1[processos]
        DB2[consultas]
        DB3[protocolos]
        DB4[movimentacoes]
    end

    subgraph SEI["SEI-RJ — página pública"]
        SEI1[md_pesq_processo_exibir.php]
    end

    C1 --> P3
    C1 --> P1
    P1 --> A1
    P1 --> A2
    P2 --> A2
    P4 --> I4
    D1 --> App
    D1 --> Infra
    A1 --> A4
    A2 --> A5
    A1 --> I1
    A1 --> I2
    A2 --> I5
    I5 --> I4
    I4 --> DB
    I1 --> SEI1

    style Cliente fill:#e1f5fe
    style Pres fill:#fff3e0
    style CC fill:#e8f5e9
    style App fill:#f3e5f5
    style Infra fill:#eceff1
    style DB fill:#ffebee
    style SEI fill:#fce4ec

## Camadas e responsabilidades

| Camada | Projeto | Responsabilidade | Depende de |
|---|---|---|---|
| `Domain` | `SeiRjMonitor.Domain` | Agregado `Processo`, entidades (`Consulta`, `ProtocoloItem`, `MovimentacaoItem`), value objects (`SeiLink`, `PaginaCarregada`, `ConsultaProcessoResultado`), utilitários (`SeiTextos`, `ClassificadorEvento`), interfaces de repositório | Nada |
| `Application` | `SeiRjMonitor.Application` | Casos de uso (`SeiConsultaService`, `SeiMonitoramentoService`), opções (`MonitoramentoOptions`), portas de saída (`ISeiPageLoader`, `ISeiPageParser`), DTOs/records de leitura (`RegistrosPersistencia`) | `Domain` |
| `Infrastructure` | `SeiRjMonitor.Infrastructure` | EF Core + Npgsql (`SeiRjMonitorDbContext`), configurações de entidade, migrations, repositórios, HTTP seguro (`SeiPageLoader`), parser (`SeiPageParser`), segurança de rede (`SegurancaRede`), opções HTTP (`SeiHttpOptions`) | `Domain`, `Application` |
| `CrossCutting` | `SeiRjMonitor.CrossCutting` | Registro de DI centralizado (`DependencyInjection`), resolução de connection string, configuração de opções | `Application`, `Infrastructure` |
| `Presentation` | `SeiRjMonitor.Presentation` | Host ASP.NET Core (`Program.cs`), ferramentas MCP (`SeiTools`), middleware de API key (`ApiKeyAuthenticationMiddleware`), serviço de monitoramento (`MonitoramentoBackgroundService`), health check (`PostgresHealthCheck`) | `CrossCutting` |
| `Tests` | `SeiRjMonitor.Tests` | Testes xUnit organizados por domínio, segurança, parser, persistência, aplicação, monitoramento e apresentação | Projetos de produção |

> Nota: o nome da pasta no repositório é `InfraStructure` (com "t" após "Infra"), preservado conforme implementação real.
```

## Componentes principais e seus papéis

| Componente | Arquivo | Papel |
|---|---|---|
| `Program.cs` | `src/Presentation/Program.cs` | Configura Kestrel, DI, MCP server, middleware, health checks, validação de segurança de startup, endpoints `/`, `/health/live`, `/health/ready`, `/mcp` |
| `SeiTools` | `src/Presentation/SeiTools.cs` | 4 ferramentas MCP (`consultar_processo`, `historico_processo`, `listar_processos_monitorados`, `validar_link_processo`); serialização JSON; tratamento de falhas de infraestrutura |
| `MonitoramentoBackgroundService` | `src/Presentation/MonitoramentoBackgroundService.cs` | `BackgroundService` que executa ciclos periódicos de consulta de todos os processos, com `SemaphoreSlim` (não sobreposição), timeout por ciclo e isolamento de falhas |
| `ApiKeyAuthenticationMiddleware` | `src/Presentation/ApiKeyAuthenticationMiddleware.cs` | Protege `/mcp` com chave de API; aceita `Authorization: Bearer` ou `X-Api-Key`; comparação SHA-256 em tempo constante |
| `PostgresHealthCheck` | `src/Presentation/PostgresHealthCheck.cs` | Readiness: verifica conectividade com PostgreSQL em até 5 s, sem expor detalhes sensíveis |
| `SeiConsultaService` | `src/Application/SeiConsultaService.cs` | Orquestra consulta: valida link via `SeiLink`, carrega página via `ISeiPageLoader`, parse via `ISeiPageParser` |
| `SeiMonitoramentoService` | `src/Application/SeiMonitoramentoService.cs` | Compõe consulta pública com persistência: cria/atualiza processo, grava consulta, faz upsert de protocolos e movimentações, calcula novidades |
| `SeiPageLoader` | `src/InfraStructure/Services/SeiPageLoader.cs` | Download HTTP da página pública com timeout, limite de bytes, redirecionamentos manuais revalidados, `ConnectCallback` anti-DNS-rebinding, proxy desabilitado |
| `SeiPageParser` | `src/InfraStructure/Services/SeiPageParser.cs` | Extração de dados do HTML com AngleSharp: tabelas `#tblCabecalho`, `#tblDocumentos`, `#tblHistorico`; leitura posicional; classificação de eventos |
| `SegurancaRede` | `src/InfraStructure/Services/SegurancaRede.cs` | Validação de endereços IP públicos; bloqueia loopback, privadas, link-local, CGNAT, multicast, documentação; aceita apenas IPv6 global `2000::/3` |
| `SeiRjMonitorDbContext` | `src/InfraStructure/Persistence/SeiRjMonitorDbContext.cs` | Contexto EF Core com `DbSet` para `Processos`, `Consultas`, `Protocolos`, `Movimentacoes`; implementa `IUnitOfWork` |
| `DependencyInjection` | `src/CrossCutting/Ioc/DependencyInjection.cs` | Registro de DbContext (Npgsql com retry transitório), opções, repositórios (scoped), serviços de aplicação (scoped), loaders/parsers (singleton) |

## Padrões efetivamente encontrados

Os seguintes padrões estão **realmente implementados** no código:

1. **Separação em camadas (Domain / Application / Infrastructure / Presentation)** — diretórios e projetos distintos com regra de dependência unidirecional.
2. **Portas e adaptadores (hexagonal simplificado)** — `ISeiPageLoader` e `ISeiPageParser` definidos em `Application/Abstractions`, implementados em `Infrastructure/Services`.
3. **Agregado de domínio** — `Processo` como raiz de agregado encapsulando `Consulta`, `ProtocoloItem` e `MovimentacaoItem`.
4. **Value object** — `SeiLink` (record imutável) valida e classifica o link público.
5. **Unit of Work** — `SeiRjMonitorDbContext` implementa `IUnitOfWork` com `SaveChangesAsync`.
6. **Repositório** — implementações EF Core para cada entidade, expostas por interfaces no Domain.
7. **BackgroundService** — `MonitoramentoBackgroundService : BackgroundService` para monitoramento periódico.
8. **Middleware de autenticação** — `ApiKeyAuthenticationMiddleware` protege o endpoint MCP.
9. **Health checks** — `IHealthCheck` para readiness (PostgreSQL) e liveness (sem dependências).
10. **MCP Server Tools** — atributo `[McpServerTool]` e `[McpServerToolType]` do `ModelContextProtocol.Server`.
11. **Retry transitório** — `EnableRetryOnFailure` do Npgsql (3 tentativas, 5 s).
12. **Split query** — `AsSplitQuery()` para evitar produto cartesiano ao carregar agregado com três coleções.
13. **Comparação em tempo constante** — `CryptographicOperations.FixedTimeEquals` para comparação de hashes de API key.
14. **CancellationToken** — propagação de cancelamento em toda a cadeia de chamadas HTTP, banco e ciclo de monitoramento.
15. **SemaphoreSlim (1,1)** — evita sobreposição de ciclos de monitoramento.

Padrões **não** implementados (evitar afirmar que estão presentes):
- Outbox pattern, message bus, event sourcing;
- CQRS completo (leitura e gravação em modelos separados, embora existam DTOs de leitura);
- Lock distribuído para múltiplas réplicas;
- Rate limit na aplicação;
- Paginação de histórico;
- Cache distribuído (Redis).

## Estrutura de diretórios relevante

```
src/
  Domain/
    Aggregates/        Processo, Consulta, ProtocoloItem, MovimentacaoItem
    Interfaces/        IProcessoRepository, IConsultaRepository, IProtocoloRepository,
                       IMovimentacaoRepository, IUnitOfWork
    SeiLink.cs         Value object de validação de link
    SeiTextos.cs       Normalização e leitura de textos do SEI-RJ
    ClassificadorEvento.cs Classificação de andamentos
    PaginaCarregada.cs Resultado do download
    ConsultaProcessoResultado.cs Resultado da consulta / tipos de falha
  Application/
    SeiConsultaService.cs Caso de uso: consulta pública
    SeiMonitoramentoService.cs Caso de uso: persistência + detecção de novidades
    MonitoramentoOptions.cs Opções de configuração do monitoramento
    Abstractions/        ISeiPageLoader, ISeiPageParser
    Persistence/         RegistrosPersistencia (DTOs/records de leitura)
  InfraStructure/
    Persistence/         SeiRjMonitorDbContext, Configurations (EF Core mappings)
    Migrations/          20261125082055_InitialCreate.cs
    Repositories/        ProcessoRepository, ConsultaRepository, ProtocoloRepository,
                         MovimentacaoRepository
    Services/            SeiPageLoader, SeiPageParser, SegurancaRede, SeiHttpOptions
  CrossCutting/
    Ioc/               DependencyInjection (registro de DI)
  Presentation/
    Program.cs         Host ASP.NET Core, MCP, middlewares, health checks
    SeiTools.cs        4 ferramentas MCP
    MonitoramentoBackgroundService.cs Serviço de monitoramento periódico
    ApiKeyAuthenticationMiddleware.cs Autenticação por chave de API
    PostgresHealthCheck.cs Health check de readiness
Test/
  XUnit/
    DomainTests/, SecurityTests/, ParserTests/, PersistenceTests/,
    ApplicationTests/, MonitoringTests/, PresentationTests/, Fixtures/
```

## Como os componentes colaboram

1. O cliente MCP chama uma ferramenta em `/mcp`.
2. O `ApiKeyAuthenticationMiddleware` valida a chave de API (se exigida).
3. `SeiTools` recebe a chamada, cria um escopo de DI e delega para `SeiMonitoramentoService`.
4. `SeiMonitoramentoService` usa `SeiConsultaService` para baixar e extrair dados da página do SEI-RJ.
5. `SeiConsultaService` valida o link com `SeiLink.TryParse`, carrega o HTML via `SeiPageLoader` (com proteções anti-SSRF) e faz parse com `SeiPageParser` (AngleSharp).
6. `SeiMonitoramentoService` persiste o resultado no `SeiRjMonitorDbContext` (PostgreSQL), calcula novidades comparando com dados existentes e retorna ao cliente.
7. O `MonitoramentoBackgroundService` executa o mesmo fluxo periodicamente para todos os processos registrados, reutilizando o link da consulta mais recente de cada um.
8. Health checks expõem liveness e readiness para orquestradores/proxies.

## Divergências em relação à documentação preexistente

O documento `docs/architecture.md` pré-existente já descreve a arquitetura de forma alinhada com o código. Nenhuma divergência relevante foi encontrada entre esse documento e a implementação inspecionada. Este novo documento complementa com mais detalhes de componentes, padrões efetivos e estrutura de diretórios, sem substituir o original.