-- Operação durável e confirmação por fotografia imutável da seleção.
ALTER TABLE admin_users ADD COLUMN must_change_password BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE discovery_targets ADD COLUMN provider VARCHAR(30) NOT NULL DEFAULT 'default',
 ADD COLUMN priority INT NOT NULL DEFAULT 1,
 ADD COLUMN refresh_minutes INT NOT NULL DEFAULT 10080,
 ADD COLUMN result_limit INT NOT NULL DEFAULT 2000;
CREATE TABLE worker_heartbeats (
 instance_id VARCHAR(80) PRIMARY KEY, started_at DATETIME(6) NOT NULL,
 heartbeat_at DATETIME(6) NOT NULL, working_set_bytes BIGINT NOT NULL,
 state VARCHAR(30) NOT NULL, discovery_enabled BOOLEAN NOT NULL, crawler_enabled BOOLEAN NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE moderation_batches (
 id CHAR(36) PRIMARY KEY, actor VARCHAR(160) NOT NULL, operation VARCHAR(30) NOT NULL,
 reason VARCHAR(500) NULL, created_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NOT NULL,
 completed_at DATETIME(6) NULL, selection_json LONGTEXT NOT NULL, result_json LONGTEXT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
