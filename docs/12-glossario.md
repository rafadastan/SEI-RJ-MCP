# Glossário — SEI-RJ Monitor MCP

Lista de termos técnicos utilizados na documentação e no código do projeto, com definições claras e no contexto do sistema.

## A

### Agregado (Domain-Driven Design)
Objeto composto que trata um conjunto de entidades associadas como uma única unidade para alterações de dados. Neste projeto, `Processo` é a raiz do agregado e encapsula `Consulta`, `ProtocoloItem` e `MovimentacaoItem`.

### API key
Chave de autenticação usada por clientes para acessar o endpoint MCP. Neste projeto, é um valor único enviado nos cabeçalhos `Authorization: Bearer` ou `X-Api-Key`.

## B

### BackgroundService
Classe base do .NET (`Microsoft.Extensions.Hosting.BackgroundService`) para executar tarefas em segundo plano hospedadas pela aplicação. Usado pelo `MonitoramentoBackgroundService` para o ciclo periódico de monitoramento.

### CAPTCHA
Teste desafio-resposta usado para distinguir humanos de máquinas. O SEI-RJ usa CAPTCHA em sua pesquisa pública, motivo pelo qual o projeto não oferece busca por número sem link.

### Chave SHA-256
Resultado de uma função de hash criptográfica SHA-256, representada como 64 caracteres hexadecimais. Usada para deduplicar protocolos e movimentações por processo.

### Cliente MCP
Aplicação que se conecta a um servidor MCP para usar suas ferramentas (ex.: MCP Inspector, IDEs com suporte a MCP, aplicações baseadas em LLM).

### Conexão completa (connection string)
String que contém todos os parâmetros de conexão com o banco (host, porta, banco, usuário, senha, SSL). Neste projeto: `ConnectionStrings:SeiMonitor`.


## D

### DDD (Domain-Driven Design)
Abordagem de desenvolvimento de software centrada no domínio de negócio. Usada de forma simplificada neste projeto com separação em camadas.

### Dedup (deduplicação)
Processo de eliminar registros duplicados. Neste projeto, protocolo e movimentação são considerados duplicados se tiverem a mesma chave SHA-256 **dentro do mesmo processo**.

### DbContext
Classe principal do Entity Framework Core que coordena a funcionalidade de entidades e o acesso ao banco de dados. Aqui: `SeiRjMonitorDbContext`.

### DNS rebinding
Ataque em que um nome de domínio público resolve primeiro para um IP público e depois para um IP privado, permitindo acesso à rede interna. Mitigado pela validação de todos os endereços DNS no `ConnectCallback`.

## E

### Endpoint
URL onde um serviço expõe funcionalidade. Exemplo: `/mcp`, `/health/live`, `/health/ready`.

### Entity Framework Core (EF Core)
Mapeador objeto-relacional (ORM) da Microsoft para .NET. Usado para persistência em PostgreSQL via Npgsql.

## H

### Health check
Endpoint que reporta o estado de saúde de um serviço ou de suas dependências. Aqui: `/health/live` (liveness) e `/health/ready` (readiness).

### Host header injection
Ataque que manipula o cabeçalho `Host` de uma requisição HTTP. Mitigado com `AllowedHosts` restrito em produção.

## I

### IHostedService
Interface do .NET para serviços executados em segundo plano pelo host. `BackgroundService` implementa essa interface.

## K

### Kestrel
Servidor web incluído no ASP.NET Core. Configurado em `Program.cs` com limites de corpo, headers e keep-alive.

## L

### Liveness
Health check que indica se o processo da aplicação está vivo, sem verificar dependências externas. Aqui: `/health/live`.

### Lock distribuído
Mecanismo de exclusão mútua entre múltiplas instâncias de um serviço. Não implementado; o projeto mantém uma única instância do worker.

### MCP (Model Context Protocol)
Protocolo aberto que padroniza como aplicações (especialmente modelos de linguagem) se conectam a ferramentas e fontes de dados.

### Middleware
Componente do pipeline do ASP.NET Core que processa requisições e respostas. Exemplo: `ApiKeyAuthenticationMiddleware`.

### Migração (migration)
Arquivo que descreve uma alteração no schema do banco de dados. EF Core gera e aplica migrations. Aqui: `20261125082055_InitialCreate`.

## N


## R

### Readiness
Health check que indica se o serviço está pronto para receber tráfego, incluindo dependências externas. Aqui: `/health/ready` (verifica PostgreSQL).

### Repositório
Classe que encapsula o acesso a dados de uma entidade. Implementado em `InfraStructure/Repositories` com interfaces no `Domain/Interfaces`.

### Resumo do ciclo
Registro de início, fim, total de processos consultados e falhas de um ciclo de monitoramento. Exposto por `GET /` e pela propriedade `UltimoCiclo` do `MonitoramentoBackgroundService`.

## S

### Schema
Estrutura do banco de dados (tabelas, colunas, índices, restrições). Definida pelas migrations EF Core.

### Segurança por camadas (defesa em profundidade)
Estratégia que aplica múltiplas proteções independentes (allowlist de host, validação de DNS, bloqueio de redirect, timeouts, limite de bytes) para mitigar SSRF.

### SemaphoreSlim
Classe do .NET para limitar o acesso simultâneo a um recurso. Usado com contagem 1 para evitar sobreposição de ciclos de monitoramento.

### SHA-256
Algoritmo de hash criptográfico que produz um resumo de 256 bits. Usado para comparação de chaves de API e deduplicação de dados.

### Smooke test
Teste rápido de validação de funcionalidade. O repositório inclui `scripts/mcp-tools.ps1` como smoke test do protocolo MCP.

### SSRF (Server-Side Request Forgery)
Ataque que força o servidor a fazer requisições para recursos internos ou externos não intencionais. Mitigado neste projeto com allowlist, validação de DNS e bloqueio de IPs privados.

### Streamable HTTP
Transporte do MCP que usa HTTP para enviar mensagens JSON-RPC e receber respostas, com suporte a streaming (SSE). Usado neste projeto em `/mcp`.

## T

### Timeout
Tempo máximo de espera por uma operação. O projeto define timeouts para requisição HTTP, conexão, leitura de resposta, comando SQL e ciclo de monitoramento.

## U

### Upsert
Operação que insere um registro se não existir ou atualiza se já existir. Usado para protocolos e movimentações com base na chave SHA-256.

### Unit of Work
Padrão que coordena múltiplas gravações em uma única transação/unidade. Implementado por `SeiRjMonitorDbContext` via `IUnitOfWork`.

## W

### Webhook
Notificação HTTP enviada automaticamente para um endpoint externo quando um evento ocorre. Não implementado neste projeto.

## Relação com outros documentos

Todos os termos acima aparecem ou são necessários para entender os documentos em [docs/](.):
- [Visão geral](01-visao-geral.md)
- [Arquitetura](02-arquitetura.md)
- [Fluxo de execução](03-fluxo-de-execucao.md)
- [Banco de dados](04-banco-de-dados.md)
- [MCP e integração](05-mcp-e-integracao.md)
- [Monitoramento automático](06-monitoramento-automatico.md)
- [Segurança](07-seguranca.md)
- [Testes](08-testes.md)
- [Configuração e deploy](09-configuracao-e-deploy.md)
- [Decisões arquiteturais](10-decisoes-arquiteturais.md)
- [Roadmap](11-roadmap.md)
### Npgsql
Provedor ADO.NET e Entity Framework Core para PostgreSQL. Usado para acessar o banco.

### Notificação externa
Envio de aviso para fora do sistema (e-mail, webhook, Telegram). Não implementado neste projeto.

## P

### Parsing / Parser
Processo de extrair dados estruturados a partir de texto não estruturado (HTML). Feito por `SeiPageParser` com a biblioteca AngleSharp.

### Polling
Consulta repetida em intervalos regulares. O monitoramento periódico usa polling para consultar processos.

### PostgreSQL
Banco de dados relacional de código aberto usado para persistência durável dos snapshots.

### Protocolo item
Registro de um protocolo/documento público associado a um processo, com chave de deduplicação.

### InfraStructure
Nome da pasta/projeto de infraestrutura no repositório (escrito com "t" após "Infra"). Contém EF Core, repositórios, HTTP, parser e migrations.

### Injeção de dependência (DI)
Padrão em que as dependências de uma classe são fornecidas externamente (contêiner DI do ASP.NET Core). Registrado em `CrossCutting/Ioc/DependencyInjection.cs`.
### Consulta (snapshot)
Registro de uma leitura da página pública do SEI-RJ, persistida na tabela `consultas`. Serve como linha do tempo de leituras de um processo.

### CORS
Cross-Origin Resource Sharing. Mecanismo que permite ou restringe páginas web de fazerem requisições a uma origem diferente. Desativado por padrão neste projeto.