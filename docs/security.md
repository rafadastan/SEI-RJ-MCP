# Segurança

## Escopo e modelo de ameaça

O serviço processa apenas informações públicas do SEI-RJ, mas expõe uma superfície de
rede que pode ser abusada para:

- acessar serviços internos da rede onde a aplicação roda (**SSRF**);
- usar o servidor como proxy para baixar conteúdo arbitrário;
- consumir recursos por respostas gigantes, redirecionamentos ou timeouts longos;
- enumerar processos e obter dados públicos em massa sem autenticação;
- vazar credenciais em logs ou arquivos versionados;
- derrubar o host por falhas não tratadas.

As mitigações abaixo estão implementadas no código. Riscos residuais e recomendações
operacionais estão no fim do documento.

## Autenticação do endpoint MCP

Implementação: `src/Presentation/ApiKeyAuthenticationMiddleware.cs`.

- Protege tudo sob `/mcp` (POST, GET, DELETE). `OPTIONS` é liberado para preflight CORS.
- Aceita `Authorization: Bearer <chave>` ou `X-Api-Key: <chave>`.
- Compara `SHA-256` dos valores com `CryptographicOperations.FixedTimeEquals`
  (comparação em tempo constante; o tamanho do hash não varia).
- Em `Production`, a ausência de `Seguranca:ApiKey` **impede o start** da aplicação.
- A chave deve ter pelo menos 32 caracteres; o valor de exemplo `change_me` é recusado.
- Em `Development`, sem chave configurada, a autenticação fica desativada e um warning
  é emitido no log. Não exponha esse ambiente.

Configuração:

```text
Seguranca__ApiKey=<chave aleatória com 32+ caracteres>
Seguranca__ExigirAutenticacao=true
```

Geração de chave:

```bash
openssl rand -base64 48
```

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

### Limitações

- Chave única e estática, sem escopos, expiração ou revogação automática. Rotacione
  manualmente (veja [operations](operations.md)).
- Não há rate limit embutido. Em produção, aplique limite no proxy/plataforma
  (ex.: 60 req/min por IP) para reduzir abuso e enumeração.
- A chave viaja no cabeçalho; use sempre HTTPS na borda.

## Defesa contra SSRF

### 1. Validação do link (`SeiLink`)

Somente:

- esquema `https`;
- host `sei.rj.gov.br` ou subdomínios (`*.sei.rj.gov.br`);
- porta 443 (explícita ou implícita);
- caminho exato `/sei/modulos/pesquisa/md_pesq_processo_exibir.php`;
- sem usuário/senha embutidos na URL.

São recusados `http`, `file`, `ftp`, IPs literais, `localhost`, domínios parecidos
(`sei.rj.gov.br.evil.com`, `evilsei.rj.gov.br`), portas alternativas e caminhos
adicionais.

### 2. Redirecionamentos

`SeiPageLoader` usa `AllowAutoRedirect = false`. Cada `Location` é revalidado pela mesma
política e resolve-se contra a URL atual; o limite padrão é 5 redirecionamentos.

### 3. Resolução DNS e conexão

Para impedir *DNS rebinding* (nome público que resolve para IP privado), o
`SocketsHttpHandler.ConnectCallback` valida **todos** os endereços retornados pelo DNS
com `SegurancaRede.EnderecoPublicoPermitido` antes de conectar. São bloqueados:

- loopback, redes privadas IPv4 (`10/8`, `172.16/12`, `192.168/16`), link-local
  (`169.254/16`, incluindo metadata `169.254.169.254`), CGNAT (`100.64/10`),
  documentação, benchmark, multicast e reservados;
- IPv6 loopback, único local (`fc00::/7`), link-local (`fe80::/10`), multicast,
  documentação e mapeamentos IPv4 privados (`::ffff:10.0.0.1`);
- IPv6 só é aceito no bloco global `2000::/3`.
- Proxies automáticos são desabilitados (`UseProxy = false`), evitando que um proxy do
  ambiente redirecione a conexão para a rede interna.

### 4. Limites de recurso

| Limite | Padrão | Configuração |
| --- | --- | --- |
| Timeout total da requisição | 30 s | `SeiMonitor:Http:TimeoutSeconds` (5–120) |
| Timeout de conexão | 10 s | `SeiMonitor:Http:ConnectTimeoutSeconds` (3–60) |
| Tamanho máximo do HTML | 5 MB | `SeiMonitor:Http:MaxResponseBytes` (64 KB–20 MB) |
| Redirecionamentos | 5 | `SeiMonitor:Http:MaxRedirects` (0–10) |
| Corpo de requisição no host | 1 MB | `Kestrel:Limits:MaxRequestBodySize` |
| Timeout de headers | 15 s | fixo no host |
| Timeout de comando SQL | 30 s | `Database:CommandTimeoutSeconds` (5–120) |
| Timeout de um ciclo | 30 min | `SeiMonitor:CycleTimeoutSeconds` |

O limite de resposta é verificado pelo `Content-Length` e também durante a leitura do
stream, para respostas sem tamanho declarado.

## Tratamento de erros e vazamento de informação

- Erros de rede do SEI são convertidos em mensagens genéricas ao cliente; detalhes
  (host, IP, porta, stack trace) ficam apenas no log do servidor.
- Em ambiente não-Development, exceções não tratadas retornam
  `application/problem+json` com mensagem genérica, sem stack trace.
- O health check de readiness não expõe host, usuário ou mensagem original do banco.
- O log de autenticação registra apenas o IP de origem e o resultado, nunca a chave.

## Cabeçalhos e CORS

O host adiciona em todas as respostas:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy: geolocation=(), microphone=(), camera=()`
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'`
- `Cache-Control: no-store`
- `Strict-Transport-Security` em ambientes não-Development

CORS é desativado por padrão. Se necessário, defina `Seguranca:AllowedOrigins` com as
origens exatas; métodos `GET, POST, DELETE, OPTIONS` e os cabeçalhos MCP são liberados.

## Gestão de segredos

- Nenhuma credencial é versionada. `appsettings.json` e `appsettings.Production.json`
  não contêm senhas.
- `appsettings.Development.json` não contém mais senha; a senha local vem de variável de
  ambiente (`SEI_DB_PASSWORD`, `SEI_MONITOR_DB_PASSWORD` ou `PGPASSWORD`) ou de uma
  connection string completa em `ConnectionStrings__SeiMonitor`.
- `Database:PasswordEnvironmentVariable` permite indicar **apenas o nome** da variável
  que contém a senha, sem copiar o valor para arquivo.
- `PGPASSFILE` é suportado: a aplicação passa apenas o caminho ao Npgsql e nunca lê o
  conteúdo do arquivo. O pgpass deve ficar fora do repositório, com permissões restritas.
- `.env` está no `.gitignore`; use `.env.example` como modelo.
- Em produção, prefira o gerenciador de segredos da plataforma (Render Secret Files,
  Railway Variables, Docker secrets ou vault).
- A connection string completa pode conter a senha. Evite logá-la; o código não a
  registra.
- Senhas com `;` quebram a connection string montada por concatenação no
  `docker-compose.yml`. Use senhas sem `;` ou configure via secret manager com o formato
  chave-valor do Npgsql.

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

Resultado na revisão: **nenhum pacote vulnerável** nos seis projetos. As versões foram
atualizadas para EF Core 8.0.34, Npgsql EF 8.0.11 e AngleSharp 1.11.0, corrigindo
avisos anteriores do `Npgsql` e de dependências transitivas.

Recomendação: executar a verificação no CI (há workflow em `.github/workflows/ci.yml`)
e revisar mensalmente.

## Dados e privacidade

- O conteúdo armazenado é o que a consulta pública exibe (número, tipo, interessados,
  unidade, protocolos e andamentos públicos).
- Processos com restrição são sinalizados, mas apenas os dados públicos exibidos são
  extraídos; anexos e conteúdo restrito não são acessados.
- Logs incluem número de processo e descrição de movimentações. Se isso for sensível
  para o seu contexto, reduza o nível para `Warning` ou remova a descrição do log.
- Faça backup do banco com o mesmo cuidado de dados pessoais; avalie retenção e
  descarte.

## Riscos residuais e recomendações

| Risco | Severidade | Mitigação recomendada |
| --- | --- | --- |
| Ausência de rate limit na aplicação | Média | Limite no proxy/plataforma por IP e por chave |
| Chave única sem rotação automática | Média | Rotação trimestral e procedimento de emergência |
| `AllowedHosts=*` por padrão | Baixa | Definir domínio real em produção (o host emite warning) |
| TLS terminado fora da aplicação | Baixa | Garantir HTTPS obrigatório no proxy e HSTS |
| Dependência do layout HTML do SEI | Média | Monitorar `LayoutDesconhecido` e testes com fixture |
| Sem lock distribuído | Baixa | Manter uma única réplica do worker |
| Logs com descrição de movimentações | Baixa | Ajustar nível/retenção conforme política de dados |
| Sem varredura SAST/DAST no CI | Baixa | Adicionar CodeQL/Trivy conforme maturidade |

## Checklist rápido de segurança antes de expor

- [x] Autenticação obrigatória em `/mcp` em Production
- [x] Chave com 32+ caracteres e comparação em tempo constante
- [x] Allowlist de host, esquema, porta e caminho
- [x] Revalidação de redirecionamentos
- [x] Bloqueio de IPs privados na resolução DNS
- [x] Limites de tamanho, timeout e redirecionamentos
- [x] Erros genéricos ao cliente
- [x] Cabeçalhos de segurança
- [x] Container não-root
- [x] Nenhum segredo versionado
- [x] Dependências sem vulnerabilidades conhecidas
- [ ] Rate limit no proxy (operacional)
- [ ] `AllowedHosts` restrito ao domínio real (operacional)
- [ ] HTTPS obrigatório na borda (operacional)
