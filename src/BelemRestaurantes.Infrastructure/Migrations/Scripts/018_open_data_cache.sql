-- name: cache e proveniencia de dados abertos OpenStreetMap
CREATE TABLE IF NOT EXISTS open_data_snapshots (
  city VARCHAR(100) NOT NULL PRIMARY KEY,
  payload LONGTEXT NOT NULL,
  fetched_at DATETIME(6) NOT NULL,
  license VARCHAR(30) NOT NULL DEFAULT 'ODbL-1.0',
  source VARCHAR(100) NOT NULL DEFAULT 'OpenStreetMap contributors'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
