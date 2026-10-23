CREATE TABLE place_reference_index (
 id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 place_name VARCHAR(160) NOT NULL,
 normalized_name VARCHAR(160) NOT NULL,
 city VARCHAR(100) NOT NULL,
 source_url VARCHAR(1800) NOT NULL,
 source_key CHAR(64) NOT NULL,
 source_kind VARCHAR(40) NOT NULL,
 publisher VARCHAR(160) NOT NULL,
 observed_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 facts_json LONGTEXT NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'pending',
 reviewed_by VARCHAR(160) NULL,
 reviewed_at DATETIME(6) NULL,
 UNIQUE KEY ux_reference_source (source_key),
 KEY ix_reference_lookup (status,city,expires_at)
) ENGINE=InnoDB;
