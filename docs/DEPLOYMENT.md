# Implantação controlada

## Pré-requisitos
.NET 10 SDK, MySQL 16+, HTTPS e proxy reverso de confiança.

1. Execute `sql/001_initial.sql` e depois `sql/002_discovery_queue.sql` no banco em ambiente protegido.
2. Crie um usuário de aplicação de menor privilégio; ele precisa de INSERT/SELECT/UPDATE nas tabelas de descoberta e SELECT nas tabelas públicas, com grants explícitos.
3. Configure `ConnectionStrings__Postgres` em segredo do deploy e **não** no repositório.
4. Em Google Cloud, ative **Places API (New)**, habilite faturamento e configure orçamento, quotas e alertas de uso.
5. Crie uma chave de Google Places restrita à API necessária e, quando suportado, ao ambiente do servidor; coloque em segredo `GOOGLE_PLACES_API_KEY`. Não use chave do Maps Javascript público no servidor.
6. Execute `dotnet build src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release`; suba o servidor com `Discovery__Enabled=false` inicialmente. Consulte `/health` e `/health/ready`.
7. No ambiente de homologação, habilite `Discovery__Enabled=true`, `Discovery__BatchSize=2`. Observe `sync_runs` e `discovery_targets` e os custos no Google Cloud.
8. **Moderação**: resultados são gravados com status `pending`. Não conceda publicação a usuários não autenticados. Enquanto o OIDC administrativo não estiver configurado, faça aprovação apenas por operador autorizado via canal administrativo de banco auditado ou implemente OIDC antes de expor o painel.
9. Antes de público: gestão completa de identidade e MFA, moderação de fotos/avaliações, revisão LGPD, políticas de cache/atribuição Google, limites de custo e testes de carga.

A cada 10 minutos o worker consulta um **lote** das tarefas devidas, não toda a base. Cada alvo tem intervalo de 12h após sucesso e 1h após falha; falhas repetidas devem futuramente usar backoff exponencial e circuit breaker.
