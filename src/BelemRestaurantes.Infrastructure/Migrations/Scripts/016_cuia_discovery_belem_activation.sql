-- name: ativacao dos alvos de descoberta de belem e fontes autorizadas
-- Requer MySQL 8.0+

-- 1. Ativa alvos prioritários de descoberta para Belém (capital gastronômica do Pará)
UPDATE discovery_targets 
SET enabled = TRUE, next_run_at = CURRENT_TIMESTAMP(6)
WHERE normalized_city = 'belem';

-- 2. Garante que os orçamentos de API não bloqueiem a descoberta em Belém quando a chave estiver presente
UPDATE api_budgets 
SET paused = FALSE, monthly_limit_requests = 1000
WHERE provider IN ('GooglePlaces', 'Gemini');

-- 3. Enfileira tarefas iniciais de varredura para fontes autorizadas de Belém
INSERT IGNORE INTO crawler_tasks (source_id, url, city, category, state, next_attempt_at)
SELECT id, base_url, 'Belém', default_category, 'pending', CURRENT_TIMESTAMP(6)
FROM crawler_sources
WHERE target_city = 'Belém' AND status = 'active';
