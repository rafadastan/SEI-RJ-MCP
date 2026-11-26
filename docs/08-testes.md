# Testes — SEI-RJ Monitor MCP

## Frameworks e organização

- Framework de testes: **xUnit**.
- Projeto de testes: `src/Test/XUnit/SeiRjMonitor.Tests.csproj`.
- Nenhuma dependência de PostgreSQL, Docker, serviço externo ou acesso real ao SEI-RJ.
- Os testes usam:
  - `BancoTemporario` (fixture): `InMemoryDatabase` do EF Core para testes de persistência e aplicação.
  - `SqliteConnection` (`DataSource=:memory:`) apenas para validar constraints únicas que o provider in-memory não aplica.
  - Handlers HTTP falsos (`HttpMessageHandler`) para testes do loader sem rede real.
  - `HtmlSei` (fixture): HTML mínimo com a mesma estrutura de tabelas do SEI-RJ para testes do parser.

## Organização dos testes por área

| Pasta | Classes de teste | O que cobre |
|---|---|---|
| `DomainTests/` | `ProcessoTests`, `SeiLinkTests` | Links válidos/inválidos, subdomínios, portas, userinfo, formatos de parâmetro, agregado Processo. |
| `SecurityTests/` | `SegurancaRedeTests`, `ApiKeyAuthenticationMiddlewareTests`, `SeiPageLoaderTests` | IPs bloqueados/permitidos (21 casos), redirecionamentos, tamanho, timeout, cancelamento, sanitização de erros, autenticação (401, chave válida, OPTIONS, rotas fora do /mcp). |
| `ParserTests/` | `SeiPageParserTests` | Extração de metadados, protocolos, movimentações, processo não encontrado, layout desconhecido. |
| `PersistenceTests/` | `PersistenciaTests` | Persistência de processo/consulta/protocolo/movimentação, dedup escopado por processo, constraint única, projeções, agendamento (link mais recente). |
| `ApplicationTests/` | `MonitoramentoServiceTests` | Casos de uso de aplicação (monitoramento). |
| `MonitoringTests/` | `MonitoramentoBackgroundServiceTests` | Ciclo de monitoramento: sucesso, falha isolada, timeout, cancelamento, não sobreposição, banco indisponível. |
| `PresentationTests/` | `SeiToolsToolsTests` | Ferramentas MCP: erro estruturado quando banco falha, propagação de cancelamento. |
| `Fixtures/` | `BancoTemporario`, `HtmlSei` | Fixtures compartilhados. |

## Cenários cobertos (confirmados pelo código)

### Domínio

### Segurança
- 21 casos de endereços IP bloqueados (loopback, privadas, link-local, CGNAT, multicast, documentação, IPv6 local, etc.) e endereços públicos permitidos.
- Autenticação: produção sem chave falha no startup; desenvolvimento sem chave permite; chave ausente/inválida retorna 401; chave válida permite; OPTIONS não exige chave; rotas fora de /mcp não exigem chave.
- Loader HTTP: bloqueia URL fora do SEI sem fazer requisição; baixa HTML válido; segue redirecionamento para mesmo domínio; bloqueia redirecionamento para destino externo e para HTTP; bloqueia excesso de redirecionamentos; retorna status HTTP em resposta não bem-sucedida; recusa resposta acima do limite por Content-Length e durante leitura sem Content-Length; trata timeout como falha HTTP; não expõe detalhes internos em falha de rede; propaga cancelamento do chamador; respeita charset iso-8859-1.

### Parser
- Extração de metadados do processo (número, tipo, data de geração, interessados, acesso restrito).
- Extração de protocolos (número, natureza, tipo, data, unidade, restrição).
- Extração de movimentações (data/hora, unidade, tipo evento, descrição).
- Retorna falha quando processo não encontrado.
- Retorna falha quando layout desconhecido.

### Persistência
- Persistência de processo, consulta, protocolo e movimentação.
- Consulta de chave de movimentação apenas no processo informado (dedup escopado).
- Consulta de chave de protocolo apenas no processo informado (dedup escopado).
- Impede movimentação duplicada no mesmo processo (constraint única via SQLite relacional).
- Retorna nulo quando processo não existe.
- Lista resumos com contagens.
- Usa fonte da consulta mais recente no agendamento.
- Ordena consultas por data.

### Aplicação
- (Conforme `MonitoramentoServiceTests` e auditoria: linha de base, novidades, idempotência, listagem e histórico.)

### Monitoramento
- Ciclo consulta processos e registra resumo.
- Falha em um processo não interrompe os demais.
- Indisponibilidade do SEI não lança exceção.
- Ciclo não inicia antes do anterior terminar (não sobreposição via SemaphoreSlim).
- Ciclo respeita timeout.
- Cancelamento não consulta processos.
- Indisponibilidade do banco não derruba o monitoramento.

### Apresentação (ferramentas MCP)
- Ferramenta retorna erro estruturado quando banco falha (`FalhaInfraestrutura`, sem senha/localhost no JSON).
- Ferramenta propaga cancelamento do chamador.
- Validação de links válidos do SEI-RJ (host, subdomínio, porta 443, caminho exato, parâmetros nomeados/opacos/sem parâmetro).
- Rejeição de links inválidos/suspeitos (HTTP, domínio malicioso, domínio parecido, caminho alternativo, userinfo, porta 8443, localhost, IP privado, file://, ftp://, não-URL, vazio, null).

## Cenários que não são cobertos

- Não há teste de integração com PostgreSQL real (usa InMemory e SQLite em memória).
- Não há teste com acesso real ao SEI-RJ (requeriria rede externa e credenciais).
- Não há teste de carga ou performance.
- Não há teste de interface gráfica (UI).
- Não há teste de deploy ou Docker (validação de imagem não executada por falta de daemon, conforme auditoria).
- Não há teste de MCP Inspector interativo (substituído pelo smoke test via script).
- Não há teste de notificações externas (não existem na implementação).
- Não há percentual de cobertura de código declarado.

## Como executar os testes

Comando validado pelo repositório e pelo CI:

```powershell
dotnet test src/Test/XUnit/SeiRjMonitor.Tests.csproj -c Release
```

No CI (`.github/workflows/ci.yml`):

```bash
dotnet test src/Test/XUnit/SeiRjMonitor.Tests.csproj -c Release --no-build --logger "console;verbosity=normal"
```

## Como interpretar resultados

- Todos os testes devem passar (verde). Nenhum teste acessa serviços externos.
- Se houver falha em `PersistenciaTests` relacionada a constraint única, isso indica que o schema de índice único não está sendo respeitado (o teste usa SQLite relacional especificamente para validar isso).
- Se houver falha em `SegurancaRedeTests`, revisar a lista de blocos de IP em `SegurancaRede.cs`.
- Se houver falha em `SeiPageParserTests`, o layout do fixture `HtmlSei` ou o parser podem ter mudado.

## Pré-requisitos

- .NET 8 SDK (ou superior).
- Nenhum banco de dados externo necessário.
- Nenhuma credencial necessária.
- Nenhum acesso à rede externa necessário.

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — camada de testes e regra de dependência.
- [Fluxo de execução](03-fluxo-de-execucao.md) — tratamento de erros e cancelamento.
- [Segurança](07-seguranca.md) — testes de SSRF e autenticação.
- [Configuração e deploy](09-configuracao-e-deploy.md) — build e execução.
- [Auditoria preexistente](audit-report.md) — resultado final: 107/107 aprovados, 0 falhas.