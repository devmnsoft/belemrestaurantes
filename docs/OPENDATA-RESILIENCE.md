# Timeouts do Discovery OpenStreetMap

O timeout HTTP continua em 40 segundos por tentativa. Há no máximo três tentativas,
com backoff e respeito a `Retry-After`. Um timeout do servidor não é cancelamento do
Worker: o cliente tenta novamente e, ao esgotar o limite, registra indisponibilidade
na fila sem substituir o snapshot existente nem declarar descoberta bem-sucedida.
O cancelamento do job/host interrompe imediatamente a consulta e a espera.

Depois de esgotar as tentativas por 408/429/500/502/503/504, erro de transporte ou
timeout, o cliente pausa chamadas externas por pelo menos dois minutos. A pausa
vale para todos os municípios no mesmo processo. Cache válido continua acessível;
snapshots vencidos não são apresentados como uma atualização bem-sucedida.
`Retry-After` superior a 30 segundos é enviado diretamente ao reagendamento, sem
manter o job bloqueado. A pausa respeita a maior data informada pelo servidor.

O orquestrador registra a indisponibilidade como aviso e mantém a contagem de
falhas nas métricas. A fila devolve o job a `pending`, com `next_attempt_at` após a
pausa, sem incrementar `attempts`; uma queda externa não provoca dead-letter.
Falhas permanentes de HTTP e erros de ingestão continuam usando o limite de
tentativas normal. A pausa em memória é reiniciada junto com o processo; a data de
reagendamento dos jobs permanece no banco. Instâncias distintas têm pausas próprias.

Para o endpoint público padrão, uma falha transitória alterna a próxima tentativa
entre `https://overpass-api.de/api/interpreter` e
`https://overpass.private.coffee/api/interpreter`. A instância alternativa consta na
[lista pública do OpenStreetMap](https://wiki.openstreetmap.org/wiki/Overpass_API#Public_Overpass_API_instances).
A consulta e a validação são as mesmas; o cache de 24 horas permanece compartilhado.

Configuração opcional para Worker e Web:

```json
{
  "OpenData": {
    "Endpoint": "https://overpass-api.de/api/interpreter",
    "TimeoutSeconds": 40,
    "FallbackEndpoints": ["https://overpass.private.coffee/api/interpreter"]
  }
}
```

`TimeoutSeconds` é limitado entre 10 e 50 segundos para manter margem no prazo de
três minutos do job. `Retry-After` longo continua sujeito ao cancelamento do job.
Uma instância própria em `Endpoint` não recebe fallback público automático;
configure explicitamente `FallbackEndpoints` para usar réplicas. Todos os endpoints
devem usar HTTPS sem credenciais na URL. A disponibilidade externa não é garantida;
produção contínua deve usar infraestrutura adequada à carga.

Depois de publicar a nova versão, reinicie o Worker. Jobs pendentes seguem o backoff
persistido; jobs já em dead-letter exigem reprocessamento pela operação existente.
