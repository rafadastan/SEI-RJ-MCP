# Visão Geral — SEI-RJ Monitor MCP

## Propósito do projeto

O **SEI-RJ Monitor** é um servidor [MCP](https://modelcontextprotocol.io) (Model Context Protocol) escrito em **C#/.NET 8** que consulta a página pública de processos do **SEI-RJ** (Sistema Eletrônico de Informações do Estado do Rio de Janeiro), grava **snapshots** em um banco **PostgreSQL** e informa o que mudou entre consultas.

O objetivo é permitir que clientes MCP (IA, IDEs, ferramentas de automação) acompanhem processos públicos do SEI-RJ de forma automática, detectando novas movimentações e protocolos, sem acesso ao conteúdo restrito do sistema.

## Problema que resolve

O SEI-RJ disponibiliza consultas públicas de processos, mas não oferece mecanismo nativo de notificação automática de mudanças. Um usuário ou sistema interessado precisa acessar periodicamente a página para verificar se houve novas andamentos ou protocolos. Este projeto automatiza essa verificação:

- Consulta a página pública por link fornecido;
- Extrai metadados, protocolos e movimentações;
- Persiste um snapshot em PostgreSQL;
- Detecta o que é novo desde a consulta anterior;
- Expõe os dados por meio de ferramentas MCP para clientes compatíveis.

## Objetivos

| Objetivo | Status |
|---|---|
| Consultar processos públicos do SEI-RJ por link | Implementado |
| Persistir snapshots em PostgreSQL | Implementado |
| Detectar movimentações e protocolos novos | Implementado |
| Expor 4 ferramentas MCP via Streamable HTTP | Implementado |
| Monitoramento periódico automático em background | Implementado |
| Autenticação por chave de API no endpoint MCP | Implementado |
| Defesa contra SSRF e redirecionamentos maliciosos | Implementado |
| Health checks de liveness e readiness | Implementado |
| Migrations EF Core aplicadas de forma controlada | Implementado |
| Notificações externas (e-mail, webhook, Telegram) | Não implementado |
| Busca por número de processo sem link | Não implementado (CAPTCHA no SEI) |
| Multiusuário / escopos de chave | Não implementado |

## Escopo e público potencial

**Público potencial:**
- Equipes jurídicas e administrativas que acompanham processos no SEI-RJ;
- Desenvolvedores que integram clientes MCP com dados do SEI-RJ;
- Operadores que precisam de histórico local de andamentos públicos.

**Escopo confirmado:**
- Apenas informações **públicas** exibidas na consulta processual do SEI-RJ;
- Transporte **Streamable HTTP** em `/mcp`;
- Armazenamento durável apenas em **PostgreSQL** (o protocolo MCP em si é stateless);
- Uma única instância do serviço de monitoramento por instalação (sem lock distribuído).

## Funcionalidades confirmadas

Com base na implementação (`src/`):

1. **`consultar_processo`** — recebe uma URL pública do SEI-RJ, valida o link, baixa o HTML, extrai metadados/protocolos/movimentações, persiste o snapshot e retorna o que mudou desde a consulta anterior.
2. **`historico_processo`** — lê o histórico local de um processo já consultado, sem nova requisição ao SEI.
3. **`listar_processos_monitorados`** — lista os processos registrados no banco com contagens de consultas e movimentações.
4. **`validar_link_processo`** — valida estruturalmente se um link pertence à consulta pública do SEI-RJ (host, esquema, porta, caminho), sem acessar o SEI.
5. **Monitoramento periódico** — `MonitoramentoBackgroundService` consulta periodicamente todos os processos registrados, reutilizando o link da última consulta, com isolamento de falhas por processo e por ciclo.
6. **Autenticação MCP** — chave de API por `Authorization: Bearer` ou `X-Api-Key`, comparação em tempo constante (SHA-256), obrigatória em Production.
7. **Proteção contra SSRF** — allowlist de host `sei.rj.gov.br` (e subdomínios), esquema HTTPS, porta 443, caminho exato, revalidação de cada redirecionamento, bloqueio de IPs privados/reservados na resolução DNS e no momento da conexão (`ConnectCallback`), proxy automático desabilitado.
8. **Health checks** — `/health/live` (sem dependências) e `/health/ready` (PostgreSQL, timeout de 5 s).
9. **Persistência** — 4 tabelas (`processos`, `consultas`, `protocolos`, `movimentacoes`), deduplicação por `(ProcessoId, Chave)` com SHA-256, índice único por processo.
10. **Migrations controladas** — nenhuma aplicação automática de schema no startup; aplicação manual via `dotnet ef database update` ou script idempotente.

## Limitações e itens não implementados

- Não há busca de processo por número sem link (a pesquisa do SEI exige CAPTCHA).
- Não há notificações externas (e-mail, webhook, Telegram).
- Um link público pode expirar; o processo passa a falhar até receber um link novo via `consultar_processo`.
- O monitoramento é sequencial por instância. Múltiplas réplicas com o mesmo banco consultariam os mesmos processos (sem lock distribuído).
- O servidor não faz TLS; termina no proxy/plataforma.
- O parser depende do layout HTML atual do SEI-RJ; mudanças de layout geram `LayoutDesconhecido`.
- Não há paginação de histórico; o carregamento é completo por processo.
- Chave de API única e estática, sem expiração ou revogação automática.
- Não há rate limit na aplicação (recomendado no proxy/plataforma).

## Visão resumida de como funciona

```mermaid
flowchart LR
    subgraph Cliente["Cliente MCP (Inspector, IDE, LLM)"]
        C1[Chamada de ferramenta MCP]
    end

    subgraph App["SEI-RJ Monitor (ASP.NET Core)"]
        C2[SeiTools - 4 ferramentas MCP]
        C3[MonitoramentoBackgroundService - ciclo periódico]
        C4[ApiKeyAuthenticationMiddleware - chave de API]
        C5[Health checks /health/live, /health/ready]
    end

    subgraph AppInt["Lógica de aplicação"]
        S1[SeiConsultaService - valida link, carrega página]
        S2[SeiMonitoramentoService - persistência, comparação, novidades]
    end

    subgraph Infra["Infraestrutura"]
        L[SeiPageLoader - HTTP seguro, anti-SSRF]
        P[SeiPageParser - AngleSharp, extração de dados]
        R[Repositórios EF Core + Npgsql]
    end

    subgraph DB[(PostgreSQL)]
        T1[processos]
        T2[consultas]
        T3[protocolos]
        T4[movimentacoes]
    end

    subgraph SEI["SEI-RJ (página pública)"]
        SEI1[md_pesq_processo_exibir.php]
    end

    C1 --> C2
    C1 --> C4
    C3 --> S2
    C2 --> S1
    C2 --> S2
    S1 --> L
    S1 --> P
    L --> SEI1
    S2 --> R
    R --> DB
    P -.-> S2

    style Cliente fill:#e1f5fe
    style App fill:#fff3e0
    style AppInt fill:#e8f5e9
    style Infra fill:#f3e5f5
    style DB fill:#ffebee
    style SEI fill:#eceff1
```

## Tecnologias principais confirmadas no repositório

| Tecnologia | Uso | Versão / fonte |
|---|---|---|
| .NET 8 / C# | Plataforma e linguagem | SDK .NET 8 |
| ASP.NET Core | Host, Kestrel, middleware, health checks | .NET 8 |
| ModelContextProtocol.Server | Servidor MCP, ferramentas, transporte Streamable HTTP | Pacote NuGet |
| EF Core 8 + Npgsql | Persistência PostgreSQL, migrations | Pacotes NuGet |
| AngleSharp | Parser HTML da página do SEI-RJ | Pacote NuGet |
| xUnit | Framework de testes | Pacote NuGet |
| Docker / Docker Compose | Imagem multi-stage e orquestração local | Dockerfile, docker-compose.yml |
| GitHub Actions | CI (build, testes, varredura de vulnerabilidades, imagem) | `.github/workflows/ci.yml` |

> Observação: versões exatas de pacotes NuGet não são listadas aqui para evitar afirmar valores que podem mudar; consulte os arquivos `.csproj` e o `SEI-RJ-Monitor.sln` para as versões exatas.

## Relação com os demais documentos

Este documento é a porta de entrada da documentação. Os detalhes de cada área estão em:

- [Arquitetura](02-arquitetura.md) — camadas, dependências, componentes e padrões.
- [Fluxo de execução](03-fluxo-de-execucao.md) — passo a passo da consulta e do monitoramento.
- [Banco de dados](04-banco-de-dados.md) — tabelas, relacionamentos, índices e migrations.
- [MCP e integração](05-mcp-e-integracao.md) — contratos das 4 ferramentas e conexão de clientes.
- [Monitoramento automático](06-monitoramento-automatico.md) — ciclo periódico do BackgroundService.
- [Segurança](07-seguranca.md) — autenticação, SSRF, limites e proteções.
- [Testes](08-testes.md) — frameworks, cenários e execução.
- [Configuração e deploy](09-configuracao-e-deploy.md) — variáveis, build, execução e implantação.
- [Decisões arquiteturais](10-decisoes-arquiteturais.md) — decisões verificadas e trade-offs.
- [Roadmap](11-roadmap.md) — o que está implementado e possíveis evoluções.
- [Glossário](12-glossario.md) — termos técnicos utilizados.

Documentação preexistente preservada: [architecture.md](architecture.md), [security.md](security.md), [deployment.md](deployment.md), [operations.md](operations.md), [audit-report.md](audit-report.md).