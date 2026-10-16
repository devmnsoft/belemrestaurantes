# Implantação controlada

## Pré-requisitos
.NET 10 SDK, MySQL 8.0+ (CHECK constraints e `SELECT ... FOR UPDATE SKIP LOCKED`), HTTPS e proxy reverso de confiança.

1. As migrações são embutidas no assembly `BelemRestaurantes.Infrastructure` e aplicadas automaticamente pelo `BelemRestaurantes.Worker` no startup (`Database:ApplyMigrations=true`, padrão). Não execute scripts SQL manuais: os arquivos em `sql/` são legados (PostgreSQL e primeira tentativa MySQL) e divergem do schema oficial.
2. Crie um usuário de aplicação de menor privilégio; ele precisa de CREATE nas tabelas (primeira aplicação), depois INSERT/SELECT/UPDATE nas tabelas de catálogo, descoberta, moderação e auditoria, com grants explícitos.
3. Configure `ConnectionStrings__MySql` em segredo do deploy e **não** no repositório.
4. Em Google Cloud, ative **Places API (New)**, habilite faturamento e configure orçamento, quotas e alertas de uso.
5. Crie uma chave de Google Places restrita à API necessária e, quando suportado, ao ambiente do servidor; coloque em segredo `GOOGLE_PLACES_API_KEY`. Não use chave do Maps Javascript público no servidor.
6. Publique a Web com `dotnet publish src/BelemRestaurantes.Web -c Release`; suba com `Discovery:Enabled=false` inicialmente (a Web não roda descoberta — o ciclo vive no Worker). Consulte `/health` e `/health/ready`.
7. No ambiente de homologação, instale o Worker como serviço Windows (`scripts/windows/install-service.ps1`) com `Discovery:Enabled=true`, `Discovery:BatchSize=2`. Observe `discovery_cycle_runs`, `discovery_jobs` e os custos no Google Cloud.
8. **Moderação**: resultados são gravados com status `pending`. Aprovação exige autenticação com MFA via `/admin/login`. O primeiro administrador é criado pelo bootstrap quando o banco ainda não tem usuários.
9. Antes de público: revisão LGPD, políticas de cache/atribuição Google, limites de custo e testes de carga.

A cada 10 minutos o worker consulta um **lote** das tarefas devidas, não toda a base. Cada alvo tem intervalo de 12h após sucesso e backoff crescente após falha; falhas repetidas vão para dead-letter (`discovery_jobs.state = dead_lettered`).
