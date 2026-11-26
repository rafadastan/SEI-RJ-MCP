# Decisões Arquiteturais — SEI-RJ Monitor MCP

Este documento registra decisões verificadas na implementação do repositório. Quando uma decisão não está documentada historicamente com um motivo oficial, o racional é descrito como **"Racional técnico inferido a partir da implementação atual"**, sem inventar motivos originais.

Para cada decisão são listados: Contexto, Decisão observada, Alternativas relevantes, Trade-offs e Status.

## 1. Separação em camadas Domain / Application / Infrastructure / Presentation

**Contexto:** O projeto cresceu com mistura de responsabilidades (ex.: Application referenciava Infrastructure diretamente, parser estático, migrations automáticas).

**Decisão observada:** Manter projetos separados por camada com regra de dependência unidirecional: Domain não depende de nada; Application depende apenas de Domain; Infrastructure depende de Domain e Application; CrossCutting depende de Application e Infrastructure; Presentation depende de CrossCutting.

**Alternativas relevantes:** Arquitetura limpa completa com camadas de interface e adaptadores separados; onion architecture com mais camadas.

**Trade-offs:** Mais projetos e interfaces pequenas; maior clareza de dependências e facilidade de testar Application com dublês.

**Status:** Implementado.

**Racional técnico inferido:** Permitir testar casos de uso sem HTTP real e evitar vazamento de detalhes de infraestrutura para a camada de aplicação.

## 2. Portas de rede/parse na Application (ISeiPageLoader, ISeiPageParser)

**Contexto:** Application precisava acessar rede e fazer parse, o que criaria dependência direta Application → Infrastructure.

**Decisão observada:** Definir interfaces `ISeiPageLoader` e `ISeiPageParser` em `Application/Abstractions`, implementadas em `Infrastructure/Services` (`SeiPageLoader`, `SeiPageParser`).

**Alternativas relevantes:** Chamar Infrastructure diretamente de Application; usar mediator pattern.

**Trade-offs:** Duas interfaces pequenas adicionais; desacoplamento que permite testes de Application sem rede.

**Status:** Implementado.

**Racional técnico inferido:** Inverter a dependência para que a camada de aplicação não conheça detalhes de HTTP ou AngleSharp.

## 3. Migrations manuais (sem Database.Migrate no startup)

**Contexto:** Aplicação aplicando migrations automaticamente ao iniciar pode causar alterações de schema não controladas em produção.

**Decisão observada:** Não há `Database.Migrate()` no startup. Migrations são aplicadas manualmente via `dotnet ef database update` ou script idempotente (`dotnet ef migrations script --idempotent`).

## 5. Transporte MCP: Streamable HTTP em /mcp

**Contexto:** O servidor precisa expor ferramentas para clientes MCP compatíveis.

**Decisão observada:** Uso de `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` com mapeamento `app.MapMcp("/mcp")`.

**Alternativas relevantes:** Transporte stdio; gRPC; WebSocket.

**Trade-offs:** Simplicidade de integração com clientes web/HTTP; requer autenticação separada no middleware.

**Status:** Implementado.

**Racional técnico inferido:** Facilitar a conexão de clientes MCP via HTTP (Inspector, navegadores, LLMs) sem exigir stdio.

## 6. Autenticação por chave de API única no endpoint MCP

**Contexto:** O endpoint `/mcp` precisava de proteção contra acesso não autenticado.

**Decisão observada:** Middleware `ApiKeyAuthenticationMiddleware` com `Authorization: Bearer` ou `X-Api-Key`; comparação SHA-256 em tempo constante (`FixedTimeEquals`); obrigatória em Production; chave mínima 32 caracteres; `change_me` recusado.

**Alternativas relevantes:** JWT/OAuth2; autenticação por certificado; autenticação do Windows.

**Trade-offs:** Simplicidade operacional; chave única sem escopos, expiração ou revogação automática.

**Status:** Implementado.

**Racional técnico inferido:** Proteger o endpoint com o mínimo de complexidade, adequado para uso por poucos clientes/configurações.

## 7. Defesa em profundidade contra SSRF

**Contexto:** O servidor baixa páginas de uma URL fornecida pelo cliente, o que abre risco de SSRF.

**Decisão observada:** Cadeia de proteções: allowlist de host `sei.rj.gov.br` + subdomínios, esquema HTTPS, porta 443, caminho exato, sem userinfo; `AllowAutoRedirect = false` com revalidação de cada redirect; `ConnectCallback` validando todos os endereços DNS como públicos (anti-rebinding); `UseProxy = false`; timeouts e limite de bytes.

**Alternativas relevantes:** Permitir qualquer domínio HTTPS; usar proxy corporativo.

**Trade-offs:** Restringe o uso ao SEI-RJ (escopo do projeto); maior complexidade do loader HTTP.

## 10. Split query (`AsSplitQuery`) no agregado Processo

**Contexto:** Carregar `Processo` com `Consultas`, `Protocolos` e `Movimentacoes` via `Include` causa produto cartesiano.

**Decisão observada:** Uso de `.AsSplitQuery()` em `ProcessoRepository.ObterPorNumeroAsync`, gerando três consultas separadas.

**Alternativas relevantes:** Carregamento explícito separado; projeção manual; não carregar coleções.

**Trade-offs:** Três consultas por leitura de processo; evita explosão de dados em memória.

**Status:** Implementado.

**Racional técnico inferido:** Evitar problemas de desempenho com agregados grandes (muitas consultas/protocolos/movimentações).

## 11. DataHora de movimentações como timestamp without time zone

**Contexto:** O SEI-RJ exibe data/hora local sem fuso horário.

**Decisão observada:** Mapeamento EF Core `.HasColumnType("timestamp without time zone")` para `MovimentacaoItem.DataHora`.

**Alternativas relevantes:** `timestamp with time zone`; armazenar apenas em UTC.

**Trade-offs:** Evita rejeição de gravação pelo Npgsql com `DateTimeKind.Unspecified`; sem conversão de fuso automática.

**Status:** Implementado.

**Racional técnico inferido:** Preservar o valor exato exibido pelo SEI-RJ sem conversão de fuso.

## 12. Servidor sem TLS (termina no proxy/plataforma)

**Contexto:** A aplicação roda atrás de um proxy ou plataforma de nuvem.

**Decisão observada:** Kestrel escuta HTTP em `8080`; `UseHsts` apenas em não-Development; não há configuração de certificado no código.

**Alternativas relevantes:** Terminar TLS no Kestrel com certificado; usar `.UseHttps()`.

**Trade-offs:** Simplicidade do container; responsabilidade de TLS delegada à infraestrutura.

**Status:** Implementado.

**Racional técnico inferido:** Manter a aplicação simples e delegar segurança de transporte ao proxy/orquestrador.

## 13. Comparação de chave de API em tempo constante

**Contexto:** Comparação simples de strings pode ser vulnerável a ataques de timing.

**Decisão observada:** `CryptographicOperations.FixedTimeEquals` sobre hashes SHA-256 da chave esperada e recebida.

**Alternativas relevantes:** Comparação direta de strings; HMAC.

**Trade-offs:** Protege contra timing attacks; requer armazenar/comparar hashes.

**Status:** Implementado.

**Racional técnico inferido:** Reduzir risco de vazamento da chave por análise de tempo de resposta.

## 14. Cache de conexão HTTP com `SocketsHttpHandler` seguro

**Contexto:** Requisições repetidas ao SEI-RJ precisam de conexão eficiente e segura.

**Decisão observada:** `SocketsHttpHandler` com `PooledConnectionLifetime` de 5 minutos, `MaxConnectionsPerServer` de 4, `NoDelay = true`, `ConnectTimeout` configurável, `ConnectCallback` personalizado.

**Alternativas relevantes:** `HttpClientHandler` padrão; conexão por requisição.

**Trade-offs:** Reutilização de conexão; controle granular de DNS e conexão.

**Status:** Implementado.

**Racional técnico inferido:** Equilibrar desempenho e segurança nas chamadas HTTP ao SEI-RJ.

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — camadas, componentes e padrões.
- [Decisões preexistentes](architecture.md) — tabela de decisões na documentação original.
- [Auditoria preexistente](audit-report.md) — seção "Arquitetura — mudanças relevantes".
- [Roadmap](11-roadmap.md) — decisões futuras e evoluções planejadas.

**Status:** Implementado.

**Racional técnico inferido:** Impedir que o servidor seja usado como proxy para acessar redes internas ou metadados de nuvem.

## 8. Monitoramento periódico com BackgroundService + SemaphoreSlim

**Contexto:** É necessário consultar processos periodicamente sem intervenção manual.

**Decisão observada:** `MonitoramentoBackgroundService : BackgroundService` com `PeriodicTimer`, `SemaphoreSlim(1,1)` para não sobreposição, `CancellationTokenSource` com `CancelAfter` para timeout de ciclo, isolamento de falhas por processo e escopo de DI por operação.

**Alternativas relevantes:** Hangfire/Quartz; Azure Functions timer; serviço externo de agendamento.

**Trade-offs:** Simplicidade (sem dependências externas); uma única instância por instalação (sem lock distribuído).

**Status:** Implementado.

**Racional técnico inferido:** Oferecer monitoramento automático com o mínimo de infraestrutura adicional.

## 9. Hash SHA-256 para deduplicação de protocolos e movimentações

**Contexto:** Precisa identificar se um protocolo ou movimentação já foi visto, sem comparar todos os campos.

**Decisão observada:** `CalcularChaveProtocolo` e `CalcularChaveMovimentacao` usam `SHA256` de campos normalizados unidos por `\u001f` (separador de unidade), armazenados como `varchar(64)` hexadecimal minúsculo.

**Alternativas relevantes:** Comparação campo a campo; hash MD5; UUID gerado pelo banco.

**Trade-offs:** Chave compacta e determinística; risco teórico de colisão (improvável com SHA-256).

**Status:** Implementado.

**Racional técnico inferido:** Detectar duplicidade de forma determinística e independente da ordem dos dados.

**Alternativas relevantes:** Aplicação automática no startup; scripts SQL manuais sem EF migrations.

**Trade-offs:** Um passo explícito no deploy; maior controle e previsibilidade do schema.

**Status:** Implementado.

**Racional técnico inferido:** Evitar alterações automáticas de schema em produção e permitir revisão do SQL antes de aplicar.

## 4. Deduplicação escopada por processo (ProcessoId, Chave)

**Contexto:** A deduplicação global de chaves de protocolo/movimentação podia suprimir itens legítimos de processos diferentes que compartilhassem a mesma chave.

**Decisão observada:** Índices únicos `ux_protocolos_processo_chave` e `ux_movimentacoes_processo_chave` em `(ProcessoId, Chave)`; verificação de existência feita com filtro por `ProcessoId`.

**Alternativas relevantes:** Índice único apenas em `Chave` (global); não deduplicar.

**Trade-offs:** Mesma chave pode existir em processos diferentes sem conflito; integridade correta por processo.

**Status:** Implementado.

**Racional técnico inferido:** Corrigir escopo de deduplicação para evitar perda de dados legítimos.