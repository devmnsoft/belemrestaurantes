CREATE TABLE IF NOT EXISTS research_jobs (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
 target_id BIGINT UNSIGNED NOT NULL,
 run_day DATE NOT NULL,
 city VARCHAR(120) NOT NULL,
 query_text VARCHAR(500) NOT NULL,
 state VARCHAR(30) NOT NULL DEFAULT 'pending',
 lease_until DATETIME(6) NULL,
 source_count INT NOT NULL DEFAULT 0,
 queued_count INT NOT NULL DEFAULT 0,
 last_status VARCHAR(60) NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 completed_at DATETIME(6) NULL,
 UNIQUE KEY uq_research_target_day(target_id,run_day),
 KEY ix_research_state(state,lease_until,id)
);
