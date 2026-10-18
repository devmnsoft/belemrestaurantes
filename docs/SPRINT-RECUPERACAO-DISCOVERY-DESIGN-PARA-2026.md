# Sprint de recuperação — sincronização real e design paraense (18/10/2026)

## Evidência do repositório
- Worker: DiscoveryHostedService limitava ciclos a >=10 minutos; CrawlerHostedService a >=5 minutos. Corrigidos para >=1 em Development.
- Worker appsettings.Development.json tinha apenas Logging. Agora Discovery e Crawler habilitados em Development.
- MySqlDiscoveryQueue.CompleteAsync impõe TargetRefreshMinutes >=10; default 10080 minutos (7 dias). **Ciclo de 1 minuto não representa revisita de cada estabelecimento a cada minuto**; preservar orçamento e evitar varredura excessiva.
- Discovery usa discovery_targets, discovery_jobs e discovery_cycle_runs; sem alvos habilitados e vencidos, o ciclo processa 0 jobs.
- DiscoveryOrchestrator registra resultados por ingestão e moderação; **não publica automaticamente novos locais**.
- Crawler depende de fontes, tarefas e permissões robots.txt, e pode retornar zero sem erro.
- Design CSS fragmentado entre site.css, design-v3.css, design-v4.css e cuia-v5.css. Não acrescentar uma quinta folha de override para cada correção.

## Plano de ação por prioridade
1. Auditoria de migrations 001–015 e integridade de banco existente, sem recriar/remover dados.
2. Diagnóstico do Worker: conexão, secrets de GooglePlaces:ApiKey, modo Development, tarefas elegíveis, crawler_sources, limites/custos, robôs.
3. Seed idempotente, administrável e versionado de alvos de cidades do Pará e categorias do catálogo; reconciliar 144 municípios reais do banco com cobertura de tarefas. Não inferir que cada município já contém restaurante.
4. Dashboard operacional com último heartbeat, último ciclo, próxima execução, contador fila por estado, falhas e alertas; botão 'Executar agora' via fila persistente, com RBAC, antiforgery, rate limit, auditoria e sem disparar API na requisição HTTP.
5. Diagnóstico ponta a ponta: Google Places → normalização/validação → deduplicação → ingestão → moderação → publicação → indexação/busca/home; destacar separados encontrados, armazenados, pendentes e publicados.
6. Gatilho de reprocessamento para jobs dead_lettered com limite, trilha de auditoria; idempotência e backoff.
7. Design system nativo do Pará: direção editorial, azulejos belenenses, grafismos de cestaria autorizados, Rio Guamá, Ver-o-Peso, tacacá, açaí e refeições regionais. Não sugerir endosso público e não usar fotografias sem permissão.
8. Refatorar homepage, busca/cidades/categorias, detalhes do restaurante, comunidade, roteiros, parceiro, login e admin. Converter CSS legado por componente, eliminar duplicação, validar layout desktop/mobile e WCAG.
9. Testes: worker a cada 1 minuto no ambiente Development (com teste que verifique configuração efetiva), sem concorrência entre ciclos; provider simulado, banco MariaDB local, falha de credencial, fila vazia, primeira ingestão, republicação e resultado público; testes reais de API somente com autorização e cotas.
10. Entrega: relatório de comandos executados e contagem REAL de ciclos, restaurantes criados, pendentes, aprovados e exibidos. Não declarar integração externa concluída sem executar.

## SQL de triagem SOMENTE LEITURA
USE belrestaurante;
SELECT COUNT(*) AS alvos, SUM(enabled=1) AS alvos_ativos, SUM(enabled=1 AND next_run_at <= UTC_TIMESTAMP(6)) AS alvos_vencidos FROM discovery_targets;
SELECT state, COUNT(*) AS qtd FROM discovery_jobs GROUP BY state;
SELECT id, started_at, completed_at, jobs_claimed, places_discovered, restaurants_created, restaurants_updated, jobs_failed FROM discovery_cycle_runs ORDER BY id DESC LIMIT 10;
SELECT status, COUNT(*) AS qtd FROM restaurants GROUP BY status;
SELECT COUNT(*) AS total_catalogo FROM restaurants;

## Cuidados
- Não colocar segredos no GitHub. Rotacionar credenciais anteriormente expostas.
- Web e Worker têm configurações de desenvolvimento e user-secrets independentes.
- Ciclo a cada minuto não significa acesso a sites externos a cada minuto; apenas processar tarefas vencidas, respeitando crawling, quotas, custos, caches, robots.txt e privacidade.
- Sem acesso ao serviço Windows ou banco do usuário não é possível verificar sincronização real a partir do GitHub.
