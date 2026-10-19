-- Camada pública Ficha/Guia, crédito de foto e memória de buscas sem resultado.
ALTER TABLE restaurants
  ADD COLUMN catalog_tier VARCHAR(16) NOT NULL DEFAULT 'ficha',
  ADD COLUMN photo_attribution VARCHAR(500) NULL,
  ADD COLUMN photo_license VARCHAR(40) NULL,
  ADD COLUMN photo_source_url VARCHAR(800) NULL;
UPDATE restaurants SET catalog_tier = 'guia' WHERE status = 'published';
CREATE INDEX ix_restaurants_tier_status ON restaurants (status, catalog_tier, id);
ALTER TABLE restaurant_media
  MODIFY media_url VARCHAR(800) NOT NULL,
  MODIFY thumbnail_url VARCHAR(800) NULL,
  MODIFY source_origin VARCHAR(500) NOT NULL,
  MODIFY attribution_text VARCHAR(500) NOT NULL;
CREATE TABLE licensed_photo_lookups (
  restaurant_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
  checked_at DATETIME(6) NOT NULL,
  outcome VARCHAR(40) NOT NULL,
  CONSTRAINT fk_photo_lookup_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE search_gaps (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  term VARCHAR(160) NOT NULL,
  city VARCHAR(80) NULL,
  result_count INT NOT NULL,
  created_at DATETIME(6) NOT NULL,
  KEY ix_search_gaps_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
