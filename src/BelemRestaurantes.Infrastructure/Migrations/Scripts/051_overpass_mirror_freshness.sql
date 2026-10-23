-- Sprint 69: última idade de dados observada por espelho Overpass (osm3s.timestamp_osm_base).
-- O Worker grava aqui, na mesma resposta que alimenta o estado sticky em memória da Sprint 67,
-- exatamente o que o espelho disse — nunca interpretação: base_timestamp NULL é idade ilegível
-- (o próprio formato do espelho), tratada conservadoramente como dado antigo. O painel
-- /admin/geocodificacao lê esta tabela para mostrar por que fichas seguem em overpass_stale_data.
CREATE TABLE IF NOT EXISTS overpass_mirror_freshness (
  endpoint VARCHAR(512) NOT NULL,
  base_timestamp DATETIME(6) NULL,
  observed_at DATETIME(6) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  PRIMARY KEY (endpoint)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
