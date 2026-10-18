-- Checkpoint por conteúdo e por banco. Uma página só é confirmada após processar todos os itens.
CREATE TABLE IF NOT EXISTS registry_import_pages (
    page_sha256 CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    record_count INT NOT NULL,
    completed_at DATETIME(6) NOT NULL,
    cycle_id BIGINT NOT NULL,
    KEY ix_registry_import_cycle (cycle_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
