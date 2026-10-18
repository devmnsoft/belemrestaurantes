-- Contadores observados por cidade/fonte; nenhum valor estimado.
CREATE TABLE IF NOT EXISTS discovery_target_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  cycle_id BIGINT UNSIGNED NOT NULL,
  job_id BIGINT UNSIGNED NOT NULL,
  city VARCHAR(100) NOT NULL,
  category VARCHAR(100) NOT NULL,
  source VARCHAR(60) NOT NULL,
  found_count INT UNSIGNED NOT NULL DEFAULT 0,
  created_count INT UNSIGNED NOT NULL DEFAULT 0,
  updated_count INT UNSIGNED NOT NULL DEFAULT 0,
  skipped_count INT UNSIGNED NOT NULL DEFAULT 0,
  failed_count INT UNSIGNED NOT NULL DEFAULT 0,
  recorded_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_target_run_cycle (cycle_id),
  KEY ix_target_run_city (city, recorded_at),
  CONSTRAINT fk_target_run_cycle FOREIGN KEY (cycle_id) REFERENCES discovery_cycle_runs(id),
  CONSTRAINT fk_target_run_job FOREIGN KEY (job_id) REFERENCES discovery_jobs(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
