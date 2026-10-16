-- name: catálogos e estrutura base do guia
-- Requer MySQL 8.0+ (CHECK constraints e SKIP LOCKED usados em outros scripts).

CREATE TABLE IF NOT EXISTS cities (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  normalized_name VARCHAR(100) NOT NULL,
  state CHAR(2) NOT NULL DEFAULT 'PA',
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_city (state, normalized_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS neighborhoods (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  city_id INT UNSIGNED NOT NULL,
  name VARCHAR(120) NOT NULL,
  normalized_name VARCHAR(120) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_neighborhood (city_id, normalized_name),
  CONSTRAINT fk_neighborhood_city FOREIGN KEY (city_id) REFERENCES cities (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS categories (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  slug VARCHAR(100) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_category_slug (slug)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS restaurants (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(160) NOT NULL,
  normalized_name VARCHAR(160) NOT NULL,
  city VARCHAR(100) NOT NULL,
  normalized_city VARCHAR(100) NOT NULL,
  neighborhood VARCHAR(120) NULL,
  neighborhood_normalized VARCHAR(120) NULL,
  category VARCHAR(100) NULL,
  address VARCHAR(350) NULL,
  latitude DECIMAL(10,7) NULL,
  longitude DECIMAL(10,7) NULL,
  website VARCHAR(350) NULL,
  instagram VARCHAR(350) NULL,
  facebook VARCHAR(350) NULL,
  tiktok VARCHAR(350) NULL,
  youtube VARCHAR(350) NULL,
  photo_url VARCHAR(350) NULL,
  google_place_id VARCHAR(255) NULL,
  created_source_url VARCHAR(350) NULL,
  price_min TINYINT UNSIGNED NULL,
  price_max TINYINT UNSIGNED NULL,
  google_rating DECIMAL(2,1) NULL,
  google_rating_count INT UNSIGNED NULL,
  status ENUM('pending','published','rejected','suspended') NOT NULL DEFAULT 'pending',
  status_before_suspend ENUM('pending','published','rejected','suspended') NOT NULL DEFAULT 'published',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  published_at DATETIME(6) NULL,
  last_seen_at DATETIME(6) NULL,
  UNIQUE KEY uq_restaurant_place (google_place_id),
  KEY ix_restaurant_published (status, normalized_city, name),
  KEY ix_restaurant_dedup (normalized_city, normalized_name),
  KEY ix_restaurant_neighborhood (neighborhood),
  KEY ix_restaurant_category (category),
  CONSTRAINT ck_restaurant_name CHECK (CHAR_LENGTH(TRIM(name)) >= 2),
  CONSTRAINT ck_restaurant_rating CHECK (google_rating IS NULL OR (google_rating >= 0 AND google_rating <= 5)),
  CONSTRAINT ck_restaurant_price CHECK (
    price_min IS NULL OR price_max IS NULL OR price_min <= price_max
  )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS suggestions (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(160) NOT NULL,
  city VARCHAR(100) NOT NULL,
  website VARCHAR(350) NULL,
  notes VARCHAR(500) NULL,
  status ENUM('pending','approved','rejected') NOT NULL DEFAULT 'pending',
  submitted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  reviewed_at DATETIME(6) NULL,
  reviewed_by VARCHAR(160) NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;