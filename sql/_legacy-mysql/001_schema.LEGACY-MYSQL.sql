-- LEGADO - NÃO APLICAR.
-- Duplicava e divergia das migrações oficiais embutidas em
-- src/BelemRestaurantes.Infrastructure/Migrations/Scripts (001-005): colunas e
-- tipos diferentes (ex.: website TEXT vs VARCHAR(350), sem normalized_name).
-- Aplicar este arquivo antes das migrações oficiais deixaria os repositórios
-- quebrados. A fonte única de schema é o MigrationRunner, executado pelo
-- BelemRestaurantes.Worker no startup (Database:ApplyMigrations=true).
CREATE TABLE IF NOT EXISTS restaurants (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 name VARCHAR(160) NOT NULL, city VARCHAR(100) NOT NULL, neighborhood VARCHAR(120),
 category VARCHAR(100), address VARCHAR(350), website TEXT, instagram TEXT,
 facebook TEXT, tiktok TEXT, youtube TEXT, photo_url TEXT,
 status ENUM('pending','published','rejected','suspended') NOT NULL DEFAULT 'pending',
 google_place_id VARCHAR(255) UNIQUE, created_source_url TEXT,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
 last_seen_at DATETIME(6), INDEX ix_restaurants_status_city(status,city,name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS suggestions (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,name VARCHAR(160) NOT NULL,
 city VARCHAR(100) NOT NULL,website TEXT,notes VARCHAR(500),
 status ENUM('pending','approved','rejected') NOT NULL DEFAULT 'pending',
 submitted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS sync_runs (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,source VARCHAR(60) NOT NULL,
 city VARCHAR(100) NOT NULL,state VARCHAR(30) NOT NULL,
 item_count INT NOT NULL DEFAULT 0,error_message VARCHAR(500),
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 INDEX ix_sync_runs_date(created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS media_assets (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 restaurant_id BIGINT UNSIGNED NOT NULL,object_key TEXT NOT NULL,
 alt_text VARCHAR(250) NOT NULL,license_type VARCHAR(80) NOT NULL,
 permission_reference TEXT NOT NULL,moderation_status VARCHAR(20) NOT NULL DEFAULT 'pending',
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 CONSTRAINT fk_media_restaurant FOREIGN KEY(restaurant_id) REFERENCES restaurants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS ratings (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,restaurant_id BIGINT UNSIGNED NOT NULL,
 user_subject VARCHAR(160) NOT NULL,stars TINYINT UNSIGNED NOT NULL,
 review_text VARCHAR(2000),status VARCHAR(20) NOT NULL DEFAULT 'pending',
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 CONSTRAINT fk_ratings_restaurant FOREIGN KEY(restaurant_id) REFERENCES restaurants(id),
 CONSTRAINT ck_stars CHECK(stars BETWEEN 1 AND 5),
 UNIQUE KEY uq_rating_subject(restaurant_id,user_subject)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS audit_events (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,actor_subject VARCHAR(160),
 action VARCHAR(80) NOT NULL,entity_type VARCHAR(80) NOT NULL,entity_id BIGINT,
 metadata JSON,occurred_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS discovery_targets (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,city VARCHAR(100) NOT NULL,
 category VARCHAR(100) NOT NULL,enabled BOOLEAN NOT NULL DEFAULT TRUE,
 next_run_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 locked_until DATETIME(6), attempts INT NOT NULL DEFAULT 0,
 last_success_at DATETIME(6),last_error VARCHAR(500),
 UNIQUE KEY uq_target(city,category),KEY idx_due(enabled,next_run_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS source_observations (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,restaurant_id BIGINT UNSIGNED NULL,
 provider VARCHAR(40) NOT NULL,external_id VARCHAR(250) NOT NULL,
 source_url TEXT,first_seen_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 last_seen_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 data_quality VARCHAR(20) NOT NULL DEFAULT 'unverified',
 UNIQUE KEY uq_source(provider,external_id),
 CONSTRAINT fk_observation_restaurant FOREIGN KEY(restaurant_id) REFERENCES restaurants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS moderation_queue (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,item_type VARCHAR(30) NOT NULL,
 item_id BIGINT NOT NULL,reason VARCHAR(240) NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'pending',
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 reviewed_at DATETIME(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
INSERT IGNORE INTO discovery_targets(city,category)
SELECT cities.city,categories.category FROM
(SELECT 'Belém' city UNION ALL SELECT 'Ananindeua' UNION ALL SELECT 'Marabá' UNION ALL SELECT 'Santarém' UNION ALL SELECT 'Salinópolis' UNION ALL SELECT 'Castanhal' UNION ALL SELECT 'Paragominas' UNION ALL SELECT 'Canaã dos Carajás' UNION ALL SELECT 'Barcarena' UNION ALL SELECT 'Bragança' UNION ALL SELECT 'Abaetetuba' UNION ALL SELECT 'Altamira' UNION ALL SELECT 'Tucuruí' UNION ALL SELECT 'Parauapebas' UNION ALL SELECT 'Cametá' UNION ALL SELECT 'Benevides' UNION ALL SELECT 'Marituba' UNION ALL SELECT 'Santa Izabel do Pará' UNION ALL SELECT 'Vigia' UNION ALL SELECT 'Capanema') cities
CROSS JOIN
(SELECT 'restaurante' category UNION ALL SELECT 'pizzaria' UNION ALL SELECT 'lanchonete' UNION ALL SELECT 'açaí' UNION ALL SELECT 'cafeteria' UNION ALL SELECT 'tacacá') categories;
