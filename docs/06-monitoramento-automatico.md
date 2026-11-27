# Monitoramento Automático — SEI-RJ Monitor MCP

## Existência e papel do serviço de monitoramento periódico

O sistema inclui um serviço de monitoramento periódico implementado como `MonitoramentoBackgroundService` (`src/Presentation/MonitoramentoBackgroundService.cs`), que herda de `Microsoft.Extensions.Hosting.BackgroundService` e é registrado como hosted service no `Program.cs`.

Seu papel é consultar periodicamente todos os processos já armazenados no banco, reutilizando o link da última consulta de cada um, detectando novas movimentações e protocolos, e registrando o resumo de cada ciclo. Ele opera de forma autônoma após a inicialização da aplicação, sem necessidade de interação do cliente MCP.

## Configuração de intervalos e parâmetros

As configurações estão na seção `SeiMonitor` e são mapeadas para `MonitoramentoOptions` (`src/Application/MonitoramentoOptions.cs`):

| Variável/configuração | Padrão | Descrição | Faixa válida |
|---|---|---|---|
| `SeiMonitor:IntervalMinutes` | `30` | Intervalo entre ciclos. `0` desativa o agendador. | 1 a 1440 min (negativo desativa) |
| `SeiMonitor:DelayBetweenProcessesSeconds` | `2` | Pausa entre processos no mesmo ciclo. | 1 a 60 s |
| `SeiMonitor:CycleTimeoutSeconds` | `1800` | Tempo máximo de um ciclo completo. | 1 a 86.400 s |
| `SeiMonitor:StartupDelaySeconds` | `10` | Espera antes do primeiro ciclo. | 0 a 300 s |

Variáveis de ambiente usam `__` como separador (ex.: `SeiMonitor__IntervalMinutes`).

Os valores são normalizados por `MonitoramentoOptions.Normalizar()`, que ajusta valores fora de faixa e retorna avisos logados no startup:
- Intervalo negativo → desativa (0).
- Intervalo acima de 1440 → limitado a 1440.
- Intervalo entre 1 e 0 → elevado a 1 (mínimo 1 minuto se habilitado).
- Pausa abaixo de 1 s → elevado a 1 s; acima de 60 s → limitado a 60 s.
- Timeout de ciclo abaixo de 1 s → elevado a 1 s.
- Atraso inicial negativo → zerado.

## Ciclo de monitoramento

Cada ciclo executa as seguintes etapas:

1. **Espera inicial:** antes do primeiro ciclo, aguarda `StartupDelaySeconds` (permitindo que banco e rede estabilizem).
2. **Timer periódico:** usa `PeriodicTimer` com o intervalo configurado.
3. **Não sobreposição:** um `SemaphoreSlim(1,1)` garante que um ciclo nunca se sobreponha ao anterior. Se o ciclo anterior ainda estiver rodando, o próximo é ignorado com um `Warning` no log.
4. **Timeout de ciclo:** um `CancellationTokenSource.CreateLinkedTokenSource` com `CancelAfter(CycleTimeoutSeconds)` limita a duração total do ciclo. Se excedido, o ciclo é cancelado e logado como `Warning`; o host continua.
5. **Listagem de processos:** consulta `ListarProcessosParaMonitoramentoAsync`, que retorna o número e o link (fonte) da consulta mais recente de cada processo, ordenado do mais antigo para o mais novo (`UltimaConsultaUtc ASC, Id ASC`).
6. **Consulta sequencial:** para cada processo, chama `ConsultarAsync(url)`. Entre processos, aplica a pausa configurada (`DelayBetweenProcessesSeconds`).
7. **Isolamento de falhas:** erro em um processo é logado (`Warning`) e o ciclo continua para os próximos. Erro inesperado no ciclo inteiro é logado (`Error`) e o host continua.
8. **Registro do resumo:** ao final, registra `ResumoCiclo` (início, fim, total de processos, quantidade de falhas), exposto no endpoint `GET /`.

## Origem dos links monitorados

Os links monitorados vêm exclusivamente de processos já registrados no banco. O agendador usa `ListarParaAgendamentoAsync`, que:

- Agrupa consultas por `ProcessoId`.
- Seleciona a consulta mais recente de cada processo (`GROUP BY` + `First`, ordenado por `ConsultadoEmUtc DESC, Id DESC`).
- Faz join com `processos` para obter o número e o link (`Fonte`) mais recente.
- Ordena do mais antigo para o mais novo, reduzindo carga sobre o SEI-RJ.

Não há busca de novos links automaticamente: apenas processos que já foram consultados pelo menos uma vez via `consultar_processo` entram no monitoramento.

## Estratégia de atualização e comparação

Cada processo monitorado é consultado com o mesmo fluxo de `consultar_processo`:

- `SeiMonitoramentoService.ConsultarAsync(url)` baixa a página, extrai dados, persiste snapshot e calcula novidades.
- A deduplicação por `(ProcessoId, Chave)` evita duplicação de protocolos e movimentações.
- Itens novos são contados e incluídos no `ResultadoPersistencia` (`NovasMovimentacoes`, `ProtocolosNovos`, `MovimentacoesNovas`).
- Na primeira consulta de um processo (que não ocorre no monitoramento, pois já existe), nada seria marcado como novo.

O serviço de monitoramento loga:
- Linha de base (quando é a primeira consulta do processo no ciclo).
- Nenhuma novidade (quando `MovimentacoesNovas == 0`).
- Quantidade de novas movimentações, com detalhes truncados a 200 caracteres por linha.

## Comportamento em falhas, timeout e cancelamento

| Situação | Comportamento |
|---|---|
| Falha de rede/SEI em um processo | Logado como `Warning`; ciclo continua para o próximo processo. |
| Falha de banco ao listar processos | Logado como `Error`; ciclo encerra sem consultar processos. |
| Timeout do ciclo (`CycleTimeoutSeconds`) | `CancellationToken` é cancelado; ciclo logado como `Warning`; próximo ciclo ocorre normalmente. |
| Cancelamento por shutdown da aplicação | `OperationCanceledException` propagada; serviço encerra graciosamente. |
| Banco indisponível | Exceção capturada; host não cai. |
| Sobreposição de ciclos | Próximo ciclo ignorado com `Warning`; `SemaphoreSlim` impede concorrência. |

## Isolamento entre processos

Cada processo é consultado de forma isolada dentro do ciclo:
- Um escopo de DI próprio é criado para cada operação (`ComMonitoramentoAsync`), garantindo `DbContext` isolado.
- Falha em um processo não impede os demais.
- Falha no ciclo inteiro não derruba o host.

## Prevenção de ciclos concorrentes

O `SemaphoreSlim(1,1)` com `WaitAsync(0, stoppingToken)` garante que apenas um ciclo execute por vez. Se o semáforo não estiver disponível, o ciclo é ignorado. Isso evita consultas duplicadas ao SEI-RJ por sobreposição.

> Não há lock distribuído. Se múltiplas réplicas da aplicação apontarem para o mesmo banco, elas consultarão os mesmos processos simultaneamente. Por decisão de simplicidade, o projeto mantém uma única instância do worker.

## Limites atuais

- Monitoramento sequencial por instância (não paralelizado).
- Uma única instância do worker por instalação (sem lock distribuído).
- Sem notificações externas automáticas (e-mail, webhook, Telegram); as novidades são gravadas no banco e expostas pelas ferramentas MCP.
- O histórico completo é carregado em memória para comparação; processos com milhares de movimentações podem ter custo crescente.
- O intervalo mínimo é 1 minuto; não há execução sob demanda via ferramenta MCP (exceto `consultar_processo` manual).

## Monitoramento automático vs notificações externas

O **monitoramento automático** neste projeto:
- Executa ciclos periódicos em background.
- Grava snapshots e detecta novidades no banco PostgreSQL.
- Loga resultados e expõe resumo em `GET /`.
- Não envia notificações para usuários externos.

**Notificações externas** (e-mail, webhook, Telegram, push):
- **Não existem** na implementação atual.
- Não são responsabilidade do servidor MCP.
- O cliente/LLM deve consultar as ferramentas MCP (`consultar_processo`, `historico_processo`, `listar_processos_monitorados`) para saber de novidades.

## Diagrama de fluxo do monitoramento

```mermaid
flowchart TD
    Start((Início da aplicação))
    Delay[Espera inicial: StartupDelaySeconds]
    Timer[PeriodicTimer a cada IntervalMinutes]
    Lock{SemaphoreSlim disponível?}
    Ignored[Ciclo ignorado - Warning no log]
    List[ListarProcessosParaMonitoramentoAsync]
    Empty{Nenhum processo?}
    ReturnEmpty[Retorna - nenhum link registrado]
    LoopStart[Para cada processo]
    Consult[ConsultarAsync(url)]
    Success{Sucesso?}
    FailLog[Log Warning - falha no processo]
    New{Novas movimentações?}
    LogNew[Log Info - novas movimentações detectadas]
    LogNone[Log Info - nenhuma novidade]
    DelayProc[Aguardar DelayBetweenProcessesSeconds]
    Next{Próximo processo?}
    Resumo[Registrar ResumoCiclo]
    End[Fim do ciclo]

    Start --> Delay
    Delay --> Timer
    Timer --> Lock
    Lock -->|Não| Ignored
    Ignored --> Timer
    Lock -->|Sim| List
    List --> Empty
    Empty -->|Sim| ReturnEmpty
    Empty -->|Não| LoopStart
    LoopStart --> Consult
    Consult --> Success
    Success -->|Não| FailLog
    FailLog --> Next
    Success -->|Sim| New
    New -->|Sim| LogNew
    New -->|Não| LogNone
    LogNew --> DelayProc
    LogNone --> DelayProc
    DelayProc --> Next
    Next -->|Sim| LoopStart
    Next -->|Não| Resumo
    Resumo --> Timer

    style Start fill:#e1f5fe
    style Timer fill:#fff3e0
    style Lock fill:#f3e5f5
    style List fill:#e8f5e9
    style Consult fill:#e8f5e9
    style Resumo fill:#e8f5e9
    style End fill:#ffebee
```

## Relação com outros documentos

- [Arquitetura](02-arquitetura.md) — componente `MonitoramentoBackgroundService`.
- [Fluxo de execução](03-fluxo-de-execucao.md) — ciclo agendado e isolamento de falhas.
- [Banco de dados](04-banco-de-dados.md) — persistência dos resultados do monitoramento.
- [Segurança](07-seguranca.md) — timeouts e limites de requisição ao SEI.
- [Operações preexistentes](operations.md) — estado do worker em `GET /` e logs do ciclo.