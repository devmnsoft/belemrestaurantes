CREATE TABLE culinary_references (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 url_hash CHAR(64) NOT NULL UNIQUE,
 canonical_url VARCHAR(800) NOT NULL,
 kind VARCHAR(32) NOT NULL,
 title VARCHAR(220) NOT NULL,
 topic VARCHAR(180) NOT NULL,
 search_text TEXT NOT NULL,
 body JSON NOT NULL,
 state VARCHAR(24) NOT NULL DEFAULT 'published',
 observed_at DATETIME(6) NOT NULL,
 KEY ix_culinary_state (state, observed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE culinary_tasks (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 url_hash CHAR(64) NOT NULL UNIQUE,
 url VARCHAR(800) NOT NULL,
 topic VARCHAR(180) NOT NULL,
 state VARCHAR(24) NOT NULL DEFAULT 'pending',
 attempts INT NOT NULL DEFAULT 0,
 next_run_at DATETIME(6) NOT NULL,
 lease_until DATETIME(6) NULL,
 last_status VARCHAR(80) NULL,
 updated_at DATETIME(6) NOT NULL,
 KEY ix_culinary_queue (state,next_run_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE culinary_search_runs (
 topic VARCHAR(180) NOT NULL,
 run_day DATE NOT NULL,
 status VARCHAR(40) NOT NULL,
 source_count INT NOT NULL DEFAULT 0,
 PRIMARY KEY(topic,run_day)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
