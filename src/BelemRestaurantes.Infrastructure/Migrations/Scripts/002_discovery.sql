-- name: fila persistente e alvos de descoberta

CREATE TABLE IF NOT EXISTS discovery_targets (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  city VARCHAR(100) NOT NULL,
  normalized_city VARCHAR(100) NOT NULL,
  category VARCHAR(100) NOT NULL,
  enabled BOOLEAN NOT NULL DEFAULT TRUE,
  next_run_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  attempts INT UNSIGNED NOT NULL DEFAULT 0,
  last_success_at DATETIME(6) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_target (normalized_city, category),
  KEY ix_target_due (enabled, next_run_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Fila durável. Sobrevive a reinício do Windows Service: nada se perde se o
-- processo cair no meio de um lote.
CREATE TABLE IF NOT EXISTS discovery_jobs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  target_id BIGINT UNSIGNED NOT NULL,
  city VARCHAR(100) NOT NULL,
  category VARCHAR(100) NOT NULL,
  state ENUM('pending','running','succeeded','failed','dead_lettered') NOT NULL DEFAULT 'pending',
  attempts INT UNSIGNED NOT NULL DEFAULT 0,
  next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  locked_by VARCHAR(80) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  CONSTRAINT fk_job_target FOREIGN KEY (target_id) REFERENCES discovery_targets (id),
  KEY ix_job_claim (state, next_attempt_at, locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Uma linha por execução de ciclo; alimenta o dashboard de execução e saúde.
CREATE TABLE IF NOT EXISTS discovery_cycle_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  instance_id VARCHAR(80) NOT NULL,
  started_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  duration_ms INT UNSIGNED NULL,
  jobs_claimed INT UNSIGNED NOT NULL DEFAULT 0,
  places_discovered INT UNSIGNED NOT NULL DEFAULT 0,
  restaurants_created INT UNSIGNED NOT NULL DEFAULT 0,
  restaurants_updated INT UNSIGNED NOT NULL DEFAULT 0,
  jobs_failed INT UNSIGNED NOT NULL DEFAULT 0,
  places_skipped INT UNSIGNED NOT NULL DEFAULT 0,
  KEY ix_cycle_started (started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  source VARCHAR(60) NOT NULL,
  city VARCHAR(100) NOT NULL,
  state VARCHAR(30) NOT NULL,
  item_count INT NOT NULL DEFAULT 0,
  error_message VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_sync_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;