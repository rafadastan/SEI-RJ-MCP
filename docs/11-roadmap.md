# Roadmap — SEI-RJ Monitor MCP

Este documento organiza o que está implementado e verificado, melhorias possíveis, itens planejados explicitamente pelo projeto/documentação e sugestões não comprometidas. Ideias são listadas como sugestões, não como compromissos.

## O que está implementado e verificado

Com base na inspeção do código e na auditoria preexistente ([audit-report.md](audit-report.md)):

- Build Release limpo (0 avisos, 0 erros).
- 107 testes aprovados, 0 falhas, sem dependência de PostgreSQL, Docker ou acesso real ao SEI-RJ.
- Quatro ferramentas MCP preservadas e validadas por smoke test via Streamable HTTP:
  - `consultar_processo`
  - `historico_processo`
  - `listar_processos_monitorados`
  - `validar_link_processo`
- Autenticação por chave de API no `/mcp` (obrigatória em Production).
- Defesa contra SSRF: allowlist de host/caminho, revalidação de redirecionamentos, bloqueio de IPs privados na resolução DNS e no `ConnectCallback`.
- Health checks de liveness (`/health/live`) e readiness (`/health/ready`).
- Migrations EF Core aplicadas de forma controlada (sem alteração automática de schema).
- Persistência em PostgreSQL com 4 tabelas e deduplicação escopada por processo.
- Serviço de monitoramento periódico em background com isolamento de falhas e não sobreposição de ciclos.
- Dockerfile multi-stage, usuário não-root, Docker Compose, CI (build, testes, vulnerabilidades, imagem).
- Documentação: README, architecture.md, security.md, deployment.md, operations.md, audit-report.md.

## Melhorias possíveis (não implementadas)

As seguintes áreas foram identificadas como possíveis pontos de evolução, com base em limitações confirmadas no código e na documentação:

- **Busca por número sem link:** não implementada porque a pesquisa do SEI exige CAPTCHA.
- **Notificações externas:** e-mail, webhook, Telegram ou push não existem; o cliente deve consultar as ferramentas MCP para saber de novidades.
- **Rate limit na aplicação:** não existe; recomendado no proxy/plataforma.
- **Lock distribuído:** não existe; múltiplas réplicas consultariam os mesmos processos.
- **Paginação de histórico:** o carregamento é completo por processo.
- **Chave de API com expiração/revogação:** chave única e estática.
- **Suporte multiusuário/escopos:** não existe.
- **TLS na aplicação:** termina no proxy/plataforma.
- **Varredura SAST/DAST no CI:** não existe; recomendada como maturidade.

## Itens planejados explicitamente pelo projeto/documentação

Não há backlog, issues com milestone ou roadmap formal versionado no repositório inspecionado. Os únicos referenciais são:

- O próprio código (funcionalidades implementadas).
- Os documentos preexistentes ([architecture.md](architecture.md), [security.md](security.md), [deployment.md](deployment.md), [operations.md](operations.md), [audit-report.md](audit-report.md)) que descrevem o estado atual e riscos residuais.
- O README.md, que lista limitações conhecidas.

Nenhum item é descrito como "planejado para a próxima versão" com compromisso de data ou escopo.

## Sugestões não comprometidas

As sugestões abaixo são **apenas ideias**, alinhadas ao estado real do projeto, mas não são compromissos do repositório:

### Notificações
- Webhook para avisar quando novas movimentações são detectadas.
- Integração com Telegram, e-mail ou outros canais de notificação.
- Esses itens exigiriam armazenamento de destinos de notificação, modelo de assinatura por processo e mecanismo de envio fora do escopo atual.

### Multiusuário
- Suporte a múltiplos usuários com chaves de API individuais e escopos por processo.
- Exigiria modelo de usuário, organização de processos por proprietário e autorização granular.

### Escalabilidade
- Lock distribuído para permitir múltiplas réplicas do worker sem consultas duplicadas.
- Paginação de histórico para processos com milhares de movimentações.

### Observabilidade
- Métricas (Prometheus) e traces distribuídos.
- Logs estruturados em JSON.

### Outras
- Suporte a busca por número via interface alternativa (se o SEI fornecer caminho sem CAPTCHA).
- Cache de respostas do SEI-RJ para reduzir chamadas repetidas.

## Relação com outros documentos

- [Visão geral](01-visao-geral.md) — funcionalidades confirmadas e limitações.
- [Arquitetura](02-arquitetura.md) — padrões e decisões atuais.
- [Decisões arquiteturais](10-decisoes-arquiteturais.md) — decisões verificadas e trade-offs.
- [Segurança](07-seguranca.md) — riscos residuais e recomendações.
- [Auditoria preexistente](audit-report.md) — veredito e pendências operacionais.