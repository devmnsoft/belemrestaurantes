-- name: cuia crawler - pesquisa autonoma na internet e descoberta gastronomica
-- Requer MySQL 8.0+

-- 1. Fontes autorizadas de rastreamento
CREATE TABLE IF NOT EXISTS crawler_sources (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(150) NOT NULL,
  base_url VARCHAR(500) NOT NULL,
  source_type ENUM('sitemap_xml', 'rss_feed', 'html_directory', 'structured_data') NOT NULL DEFAULT 'sitemap_xml',
  target_city VARCHAR(100) NULL,
  default_category VARCHAR(100) NULL,
  status ENUM('active', 'paused', 'blocked', 'pending_review') NOT NULL DEFAULT 'active',
  robots_txt_status ENUM('allowed', 'disallowed', 'unknown') NOT NULL DEFAULT 'unknown',
  crawl_delay_seconds INT UNSIGNED NOT NULL DEFAULT 5,
  last_crawled_at DATETIME(6) NULL,
  last_success_at DATETIME(6) NULL,
  last_error VARCHAR(500) NULL,
  etag VARCHAR(100) NULL,
  last_modified_header VARCHAR(100) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_crawler_source_url (base_url),
  KEY ix_crawler_source_status (status, last_crawled_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Fila persistente de URLs para rastreamento
CREATE TABLE IF NOT EXISTS crawler_tasks (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  source_id BIGINT UNSIGNED NOT NULL,
  url VARCHAR(500) NOT NULL,
  city VARCHAR(100) NOT NULL DEFAULT 'Belém',
  category VARCHAR(100) NULL,
  state ENUM('pending', 'running', 'succeeded', 'failed', 'skipped_robots') NOT NULL DEFAULT 'pending',
  depth INT UNSIGNED NOT NULL DEFAULT 0,
  attempts INT UNSIGNED NOT NULL DEFAULT 0,
  next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  locked_by VARCHAR(80) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  CONSTRAINT fk_crawler_task_source FOREIGN KEY (source_id) REFERENCES crawler_sources (id) ON DELETE CASCADE,
  UNIQUE KEY uq_crawler_task_url (url),
  KEY ix_crawler_task_claim (state, next_attempt_at, locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Telemetria e auditoria de execucoes de rastreamento
CREATE TABLE IF NOT EXISTS crawler_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  source_id BIGINT UNSIGNED NOT NULL,
  url VARCHAR(500) NOT NULL,
  status_code INT NOT NULL DEFAULT 0,
  duration_ms INT UNSIGNED NOT NULL DEFAULT 0,
  items_discovered INT UNSIGNED NOT NULL DEFAULT 0,
  items_ingested INT UNSIGNED NOT NULL DEFAULT 0,
  error_message VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_crawler_run_source FOREIGN KEY (source_id) REFERENCES crawler_sources (id) ON DELETE CASCADE,
  KEY ix_crawler_run_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Insercao de fontes autorizadas iniciais para pesquisa gastronomica no Para
INSERT IGNORE INTO crawler_sources (name, base_url, source_type, target_city, default_category, status, crawl_delay_seconds)
VALUES 
  ('Guia Gastronômico de Belém - Diretório Oficial', 'https://belemrestaurantes.com.br/sitemap.xml', 'sitemap_xml', 'Belém', 'Regional', 'active', 5),
  ('Polo Turístico e Gastronômico do Tapajós', 'https://turismo.santarem.pa.gov.br/gastronomia/rss', 'rss_feed', 'Santarém', 'Peixaria', 'active', 5),
  ('Circuito Gastronômico de Castanhal', 'https://castanhal.pa.gov.br/sitemap-gastronomia.xml', 'sitemap_xml', 'Castanhal', 'Regional', 'active', 5),
  ('Roteiro da Culinária Marajoara (Soure e Salvaterra)', 'https://marajo.tur.br/onde-comer/sitemap.xml', 'sitemap_xml', 'Soure', 'Regional', 'active', 5);
