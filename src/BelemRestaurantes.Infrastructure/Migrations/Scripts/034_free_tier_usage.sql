CREATE TABLE IF NOT EXISTS external_free_usage (
    service VARCHAR(64) NOT NULL,
    period CHAR(8) NOT NULL,
    requests INT NOT NULL DEFAULT 0,
    last_used_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (service, period)
) ENGINE=InnoDB;
