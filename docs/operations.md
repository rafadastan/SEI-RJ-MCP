# Operação

## Supervisão rápida

| Sinal | Onde | O que indica |
| --- | --- | --- |
| Processo vivo | `GET /health/live` | Host ASP.NET Core respondendo |
| Banco disponível | `GET /health/ready` | PostgreSQL responde em até 5 s |
| Estado do worker | `GET /` | Intervalo, timeout e resumo do último ciclo |
| Autenticação ativa | log de startup | Se `Seguranca:ApiKey` está configurada |
| Falhas do ciclo | log `MonitoramentoBackgroundService` | Processos que falharam e o motivo |

O `GET /` retorna algo como:

```json
{
  "status": "running",
  "monitoramento": {
    "habilitado": true,
    "intervaloMinutos": 30,
    "tempoLimiteCicloMinutos": 30,
    "ultimoCiclo": {
      "iniciadoEmUtc": "2026-11-26T12:00:00Z",
      "concluidoEmUtc": "2026-11-26T12:00:08Z",
      "processosConsultados": 12,
      "falhas": 1
    }
  }
}
```

## Logs

Formato padrão do ASP.NET Core (console). Eventos relevantes:

- `Startup`: ambiente, endpoints e se a autenticação MCP está ativa.
- `MonitoramentoBackgroundService`:
  - início e fim de ciclo com duração, total de processos e falhas;
  - falha isolada por processo (`Warning`), com tipo e mensagem;
  - exceção inesperada no ciclo (`Error`), sem derrubar o host;
  - ciclo ignorado por sobreposição (`Warning`).
- `SeiPageLoader`: timeout, resposta grande e falha de rede (sem detalhes ao cliente).
- `ApiKeyAuthenticationMiddleware`: requisição recusada, com IP de origem.
- EF Core: erros de conexão/comando (sem a senha).

Nunca há log da chave de API nem da connection string. Em produção, o nível padrão é
`Information`; para reduzir volume, use `Warning` para `Microsoft.*` e
`SeiRjMonitor.Presentation.MonitoramentoBackgroundService` conforme necessário.

## Ciclo de monitoramento

- Desativado com `SeiMonitor:IntervalMinutes=0`.
- Primeiro ciclo após `StartupDelaySeconds`.
- Um ciclo por vez; se o anterior ainda estiver rodando, o próximo é ignorado com
  warning.
- Timeout por ciclo (`CycleTimeoutSeconds`); ao exceder, o ciclo é cancelado e o próximo
  ocorre normalmente.
- Processos são consultados do mais antigo para o mais novo, com pausa de
  `DelayBetweenProcessesSeconds`.

### Ajuste de capacidade

O tempo do ciclo cresce linearmente com o número de processos:

```
duração ≈ N × (tempo médio do SEI + pausa entre processos)
```

Se os ciclos começarem a ser ignorados por sobreposição, aumente
`SeiMonitor:IntervalMinutes`, reduza a pausa (mínimo 1 s) ou divida os processos entre
instâncias — lembrando que **não há lock distribuído**, então múltiplas réplicas
consultariam os mesmos processos.

## Backup e restauração

### Docker Compose

```bash
# Backup comprimido
docker compose exec -T postgres pg_dump -U "${POSTGRES_USER:-sei_monitor_app}" \
  -d "${POSTGRES_DB:-sei_monitor}" -Fc > backup_sei_monitor_$(date +%Y%m%d).dump

# Restauração em banco vazio
docker compose exec -T postgres pg_restore -U "${POSTGRES_USER:-sei_monitor_app}" \
  -d "${POSTGRES_DB:-sei_monitor}" --clean --if-exists < backup_sei_monitor_YYYYMMDD.dump
```

### Banco gerenciado

Use o backup automático da plataforma e teste a restauração periodicamente em um banco
descartável. Retenção sugerida: 7 diários + 4 semanais, ajustável à política de dados.

### Após restaurar

1. Rode `/health/ready`.
2. Verifique `listar_processos_monitorados`.
3. Confira o resumo do próximo ciclo em `GET /`.
4. Se o link de algum processo tiver expirado, reconsultar via `consultar_processo`.

## Rotação da chave de API

1. Gere uma nova chave (32+ caracteres).
2. Atualize o secret no ambiente e reinicie a aplicação (a chave é lida no startup).
3. Atualize os clientes MCP.
4. Confirme 401 para a chave antiga e 200 para a nova.

Não há período de convivência com duas chaves; planeje a janela de manutenção ou aceite
breve indisponibilidade de autenticação.

## Runbook de incidentes

### `/health/ready` retorna 503

1. Confirme o PostgreSQL (processo, conexões, disco, failover).
2. Teste a connection string a partir do host da aplicação.
3. Verifique se houve mudança de senha, TLS ou firewall.
4. O worker continua tentando; após o banco voltar, o próximo ciclo se recupera.
5. Se o problema for de schema, valide se as migrations foram aplicadas.

### `/mcp` retorna 401

1. Confirme o cabeçalho (`Authorization: Bearer ...` ou `X-Api-Key`).
2. Verifique se `Seguranca:ApiKey` no servidor é a mesma chave do cliente.
3. Confirme `Seguranca:ExigirAutenticacao` e o ambiente (`Production` exige chave).
4. Veja o log do middleware: ele registra a recusa, mas não a chave.

### `ProcessoNaoEncontrado` repetido

O link público expirou ou o processo saiu do ar. Peça um link novo e chame
`consultar_processo`; a linha de base e o histórico anterior são preservados.

### `LayoutDesconhecido`

O HTML do SEI mudou. Verifique o fixture
`src/Test/XUnit/Fixtures/sei_sample.html`, ajuste o parser e adicione um teste. Enquanto
isso, as consultas falham de forma isolada e o restante do monitoramento continua.

### Timeouts frequentes ao SEI

1. Verifique conectividade e latência até `sei.rj.gov.br`.
2. Aumente `SeiMonitor:Http:TimeoutSeconds` (até 120) e `CycleTimeoutSeconds`.
3. Reduza a frequência (`IntervalMinutes`) para aliviar a carga.
4. Se o SEI estiver instável, o comportamento esperado é falha isolada por processo,
   com recuperação no ciclo seguinte.

### Aplicação não inicia

Causas prováveis e mensagens:

- `Connection string 'ConnectionStrings:SeiMonitor' não foi configurada` → defina a
  variável de ambiente, informe a senha em `SEI_DB_PASSWORD`/`PGPASSWORD` ou aponte
  `PGPASSFILE` para um arquivo válido.
- `Seguranca:ApiKey é obrigatória em produção` → defina o secret.
- `Seguranca:ApiKey deve ter pelo menos 32 caracteres` → gere uma chave maior.
- Container unhealthy → verifique se a porta 8080 está correta e se `/health/live`
  responde internamente.

### Build falha com "arquivo bloqueado por outro processo"

Uma instância anterior (`dotnet run` ou `SeiRjMonitor.Presentation.exe`) mantém as DLLs
de `bin/Debug` abertas. Encerre apenas os processos deste projeto:

```powershell
powershell -NoProfile -File scripts/stop-dev.ps1
dotnet build SEI-RJ-Monitor.sln
```

Se a mensagem indicar outro processo (por exemplo Visual Studio ou `dotnet watch`),
pare a sessão de depuração correspondente antes de compilar. Não é necessário limpar
`bin`/`obj`.

### Ciclos ignorados por sobreposição

Aumente o intervalo ou o timeout do ciclo; investigue processos com latência alta. O
worker nunca executa dois ciclos ao mesmo tempo por design.

## Manutenção

- **Diário**: verificar `/health/ready`, falhas do último ciclo e espaço em disco.
- **Semanal**: revisar logs de erro e backups.
- **Mensal**: `dotnet list package --vulnerable --include-transitive`, atualizar patches,
  testar restauração.
- **Trimestral**: rotacionar a chave de API e revisar `AllowedHosts`/CORS.

## Consultas úteis (somente leitura)

```sql
-- Processos monitorados e última consulta
SELECT p."Numero", p."UltimaConsultaUtc", COUNT(c."Id") AS consultas
FROM processos p LEFT JOIN consultas c ON c."ProcessoId" = p."Id"
GROUP BY p."Id" ORDER BY p."UltimaConsultaUtc" DESC;

-- Tamanho das tabelas
SELECT relname, pg_size_pretty(pg_total_relation_size(relid)) AS tamanho
FROM pg_catalog.pg_statio_user_tables ORDER BY pg_total_relation_size(relid) DESC;

-- Movimentações detectadas nas últimas 24h
SELECT COUNT(*) FROM movimentacoes WHERE "DetectadaEmUtc" > now() - interval '24 hours';
```

## Atualização de versão

1. Faça backup.
2. Gere e revise o script de migration.
3. Aplique a migration (compatível com N-1).
4. Faça deploy da nova imagem.
5. Valide `/health/live`, `/health/ready`, `tools/list` e uma consulta real.
6. Monitore o primeiro ciclo completo.
