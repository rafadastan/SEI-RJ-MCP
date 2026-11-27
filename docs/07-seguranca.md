# Segurança — SEI-RJ Monitor MCP

Este documento resume o modelo de segurança e as proteções implementadas no código. Para detalhes operacionais (runbook, rotação de chave, backup), consulte [operations.md](operations.md) e [deployment.md](deployment.md). O documento de auditoria preexistente está em [audit-report.md](audit-report.md).

## Modelo de segurança

O serviço processa apenas informações **públicas** do SEI-RJ, mas expõe uma superfície de rede que pode ser abusada para:

- Acessar serviços internos da rede onde a aplicação roda (**SSRF**).
- Usar o servidor como proxy para baixar conteúdo arbitrário.
- Consumir recursos por respostas gigantes, redirecionamentos ou timeouts longos.
- Enumerar processos e obter dados públicos em massa sem autenticação.
- Vazar credenciais em logs ou arquivos versionados.
- Derrubar o host por falhas não tratadas.

As mitigações abaixo estão implementadas no código.

## Autenticação e autorização

Implementação: `src/Presentation/ApiKeyAuthenticationMiddleware.cs`.

- Protege tudo sob `/mcp` (POST, GET, DELETE). `OPTIONS` é liberado para preflight CORS.
- Aceita `Authorization: Bearer <chave>` ou `X-Api-Key: <chave>`.
- Compara `SHA-256` dos valores com `CryptographicOperations.FixedTimeEquals` (comparação em tempo constante; o tamanho do hash não varia).
- Em `Production`, a ausência de `Seguranca:ApiKey` **impede o start** da aplicação (validação tanto no middleware quanto em `Program.cs`).
- A chave deve ter pelo menos 32 caracteres; o valor de exemplo `change_me` é recusado.
- Em `Development`, sem chave configurada, a autenticação fica desativada e um warning é emitido no log. Não exponha esse ambiente publicamente.

Configuração:

```text
Seguranca__ApiKey=<chave aleatória com 32+ caracteres>
Seguranca__ExigirAutenticacao=true
```

Geração de chave (exemplo, não use em produção):

```bash
openssl rand -base64 48

## Validação de URL e host

O value object `SeiLink` (`src/Domain/SeiLink.cs`) valida estruturalmente o link antes de qualquer acesso à rede:

- Esquema obrigatório: `https`.
- Host permitido: `sei.rj.gov.br` ou subdomínios (`*.sei.rj.gov.br`).
- Porta: 443 (explícita ou implícita).
- Caminho exato: `/sei/modulos/pesquisa/md_pesq_processo_exibir.php`.
- Sem usuário/senha embutidos na URL (`UserInfo` vazio).

São recusados: `http`, `file`, `ftp`, IPs literais, `localhost`, domínios parecidos (`sei.rj.gov.br.evil.com`, `evilsei.rj.gov.br`), portas alternativas e caminhos adicionais.

A validação é aplicada duas vezes: no `SeiConsultaService` antes de carregar a página e no `SeiPageLoader` como defesa em profundidade (incluindo cada redirecionamento).

## Proteções contra SSRF e DNS

### Redirecionamentos

`SeiPageLoader` usa `AllowAutoRedirect = false`. Cada `Location` é revalidado pela mesma política e resolve-se contra a URL atual. O limite padrão é 5 redirecionamentos (configurável, 0–10). Redirecionamentos para fora do SEI-RJ são bloqueados.

### Resolução DNS e conexão (`ConnectCallback`)

Para impedir **DNS rebinding** (nome público que resolve para IP privado), o `SocketsHttpHandler.ConnectCallback` valida **todos** os endereços retornados pelo DNS com `SegurancaRede.EnderecoPublicoPermitido` antes de conectar. São bloqueados:

- Loopback, redes privadas IPv4 (`10/8`, `172.16/12`, `192.168/16`), link-local (`169.254/16`, incluindo metadata `169.254.169.254`), CGNAT (`100.64/10`), documentação, benchmark, multicast e reservados.
- IPv6 loopback, único local (`fc00::/7`), link-local (`fe80::/10`), multicast, documentação e mapeamentos IPv4 privados (`::ffff:10.0.0.1`).
- IPv6 só é aceito no bloco global `2000::/3`.

Proxy automático é desabilitado (`UseProxy = false`), evitando que um proxy do ambiente redirecione a conexão para a rede interna.
```

### Limitações da autenticação

- Chave única e estática, sem escopos, expiração ou revogação automática. Rotacione manualmente (veja operations.md).
- Não há rate limit embutido. Em produção, aplique limite no proxy/plataforma (ex.: 60 req/min por IP).
- A chave viaja no cabeçalho; use sempre HTTPS na borda.

## Limites de requisição e tamanho de resposta

| Limite | Padrão | Configuração | Faixa |
|---|---|---|---|
| Timeout total da requisição ao SEI | 30 s | `SeiMonitor:Http:TimeoutSeconds` | 5–120 s |
| Timeout de conexão TCP | 10 s | `SeiMonitor:Http:ConnectTimeoutSeconds` | 3–60 s |
| Tamanho máximo do HTML | 5 MB | `SeiMonitor:Http:MaxResponseBytes` | 64 KB–20 MB |
| Redirecionamentos | 5 | `SeiMonitor:Http:MaxRedirects` | 0–10 |
| Corpo de requisição no host | 1 MB | `Kestrel:Limits:MaxRequestBodySize` | — |
| Timeout de headers | 15 s | fixo no Kestrel | — |
| Timeout de comando SQL | 30 s | `Database:CommandTimeoutSeconds` | 5–120 s |
| Timeout de um ciclo de monitoramento | 30 min | `SeiMonitor:CycleTimeoutSeconds` | — |

O limite de resposta é verificado pelo `Content-Length` e também durante a leitura do stream, para respostas sem tamanho declarado.

## Tratamento de conteúdo externo como não confiável

- O HTML do SEI-RJ é tratado como dados não estruturados; o parser extrai apenas tabelas conhecidas (`#tblCabecalho`, `#tblDocumentos`, `#tblHistorico`).
- Erros de rede do SEI são convertidos em mensagens genéricas ao cliente; detalhes (host, IP, porta, stack trace) ficam apenas no log do servidor.
- Em ambiente não-Development, exceções não tratadas retornam `application/problem+json` com mensagem genérica, sem stack trace.
- O health check de readiness não expõe host, usuário ou mensagem original do banco.
- O log de autenticação registra apenas o IP de origem e o resultado, nunca a chave.
- Descrições de movimentações são truncadas a 200 caracteres no log do ciclo de monitoramento.

## Cabeçalhos de segurança e CORS

O host adiciona em todas as respostas:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy: geolocation=(), microphone=(), camera=()`
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'`
- `Cache-Control: no-store`
- `Strict-Transport-Security` em ambientes não-Development

CORS é desativado por padrão. Se necessário, defina `Seguranca:AllowedOrigins` com as origens exatas; métodos `GET, POST, DELETE, OPTIONS` e os cabeçalhos MCP são liberados.

## Gestão de segredos

- Nenhuma credencial é versionada. `appsettings.json` e `appsettings.Production.json` não contêm senhas.
- `appsettings.Development.json` não contém senha; a senha local vem de variável de ambiente (`SEI_DB_PASSWORD`, `SEI_MONITOR_DB_PASSWORD` ou `PGPASSWORD`) ou de uma connection string completa em `ConnectionStrings__SeiMonitor`.
- `Database:PasswordEnvironmentVariable` permite indicar apenas o nome da variável que contém a senha, sem copiar o valor para arquivo.
- `PGPASSFILE` é suportado: a aplicação passa apenas o caminho ao Npgsql e nunca lê o conteúdo do arquivo.
- `.env` está no `.gitignore`; use `.env.example` como modelo.
- Em produção, prefira o gerenciador de segredos da plataforma (Render Secret Files, Railway Variables, Docker secrets ou vault).
- A connection string completa pode conter a senha. Evite logá-la; o código não a registra.
- Senhas com `;` quebram a connection string montada por concatenação no `docker-compose.yml`. Use senhas sem `;` ou configure via secret manager com o formato chave-valor do Npgsql.

## TLS e responsabilidades de infraestrutura

- A aplicação escuta HTTP em `8080`. Não faz TLS.
- Em produção, termine TLS no proxy/plataforma e encaminhe para `http://app:8080`.
- Force redirect HTTP → HTTPS na borda.
- Mantenha HSTS habilitado (a aplicação já envia em Production).
- Não publique a porta do PostgreSQL.

## Container

- Runtime baseado em `mcr.microsoft.com/dotnet/aspnet:8.0`.
- Processo executado como usuário sem privilégios (`app`, UID 1654).
- `no-new-privileges` habilitado no Compose.
- Apenas a porta 8080 é exposta; o PostgreSQL fica restrito ao loopback do host.
- A imagem instala apenas `curl`, usado pelo health check.
- `.dockerignore` exclui `.git`, `.env`, `data`, `bin`, `obj`, docs e scripts.

## Dependências

Verificação executada com:

```bash
dotnet list SEI-RJ-Monitor.sln package --vulnerable --include-transitive
```

Resultado na revisão: nenhum pacote vulnerável nos seis projetos. As versões foram atualizadas para EF Core 8.0.34, Npgsql EF 8.0.11 e AngleSharp 1.11.0 (conforme auditoria).

Recomendação: executar a verificação no CI (há workflow em `.github/workflows/ci.yml`) e revisar mensalmente.

## Riscos residuais e recomendações

| Risco | Severidade | Mitigação recomendada |
|---|---|---|
| Ausência de rate limit na aplicação | Média | Limite no proxy/plataforma por IP e por chave |
| Chave única sem rotação automática | Média | Rotação trimestral e procedimento de emergência |
| `AllowedHosts=*` por padrão | Baixa | Definir domínio real em produção (o host emite warning) |
| TLS terminado fora da aplicação | Baixa | Garantir HTTPS obrigatório no proxy e HSTS |
| Dependência do layout HTML do SEI | Média | Monitorar `LayoutDesconhecido` e testes com fixture |
| Sem lock distribuído | Baixa | Manter uma única réplica do worker |
| Logs com descrição de movimentações | Baixa | Ajustar nível/retenção conforme política de dados |
| Sem varredura SAST/DAST no CI | Baixa | Adicionar CodeQL/Trivy conforme maturidade |

## Checklist rápido de segurança antes de expor

- [ ] Autenticação obrigatória em `/mcp` em Production
- [ ] Chave com 32+ caracteres e comparação em tempo constante
- [ ] Allowlist de host, esquema, porta e caminho
- [ ] Revalidação de redirecionamentos
- [ ] Bloqueio de IPs privados na resolução DNS
- [ ] Limites de tamanho, timeout e redirecionamentos
- [ ] Erros genéricos ao cliente
- [ ] Cabeçalhos de segurança
- [ ] Container não-root
- [ ] Nenhum segredo versionado
- [ ] Dependências sem vulnerabilidades conhecidas
- [ ] Rate limit no proxy (operacional)
- [ ] `AllowedHosts` restrito ao domínio real (operacional)
- [ ] HTTPS obrigatório na borda (operacional)

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — camada Presentation e middleware de autenticação.
- [Fluxo de execução](03-fluxo-de-execucao.md) — entrada de requisição MCP e autenticação.
- [Banco de dados](04-banco-de-dados.md) — conexão e senhas.
- [MCP e integração](05-mcp-e-integracao.md) — autenticação de clientes MCP.
- [Monitoramento automático](06-monitoramento-automatico.md) — timeouts e limites de ciclo.
- [Configuração e deploy](09-configuracao-e-deploy.md) — variáveis de segurança e TLS.
- [Segurança preexistente](security.md) — modelo de ameaça detalhado e mitigações.
- [Auditoria preexistente](audit-report.md) — achados de segurança e tratamento.