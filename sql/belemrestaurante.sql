-- ===========================================================
-- belemrestaurante.sql - Script UNIFICADO de banco de dados
-- Belém Restaurantes - Sprint 02 (Cuia IA / automação gastronômica)
-- Execute UMA VEZ em MySQL 8.0+ (ou MariaDB 10.6+).
-- Gerado a partir das 7 migrações embutidas (001..007).
-- ===========================================================

CREATE DATABASE IF NOT EXISTS belrestaurante
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE belrestaurante;


-- ===========================================================
-- MIGRAÇÃO: 001_CORE_CATALOG.SQL
-- ===========================================================
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

-- ===========================================================
-- MIGRAÇÃO: 002_DISCOVERY.SQL
-- ===========================================================
-- name: fila persistente e alvos de descoberta

CREATE TABLE IF NOT EXISTS discovery_targets (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  city VARCHAR(100) NOT NULL,
  normalized_city VARCHAR(100) NOT NULL,
  category VARCHAR(100) NOT NULL,
  enabled BOOLEAN NOT NULL DEFAULT TRUE,
  next_run_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  attempts INT UNSIGNED NOT NULL DEFAULT 0,
  last_success_at DATETIME(6) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_target (normalized_city, category),
  KEY ix_target_due (enabled, next_run_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Fila durável. Sobrevive a reinício do Windows Service: nada se perde se o
-- processo cair no meio de um lote.
CREATE TABLE IF NOT EXISTS discovery_jobs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  target_id BIGINT UNSIGNED NOT NULL,
  city VARCHAR(100) NOT NULL,
  category VARCHAR(100) NOT NULL,
  state ENUM('pending','running','succeeded','failed','dead_lettered') NOT NULL DEFAULT 'pending',
  attempts INT UNSIGNED NOT NULL DEFAULT 0,
  next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  locked_by VARCHAR(80) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  CONSTRAINT fk_job_target FOREIGN KEY (target_id) REFERENCES discovery_targets (id),
  KEY ix_job_claim (state, next_attempt_at, locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Uma linha por execução de ciclo; alimenta o dashboard de execução e saúde.
CREATE TABLE IF NOT EXISTS discovery_cycle_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  instance_id VARCHAR(80) NOT NULL,
  started_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  duration_ms INT UNSIGNED NULL,
  jobs_claimed INT UNSIGNED NOT NULL DEFAULT 0,
  places_discovered INT UNSIGNED NOT NULL DEFAULT 0,
  restaurants_created INT UNSIGNED NOT NULL DEFAULT 0,
  restaurants_updated INT UNSIGNED NOT NULL DEFAULT 0,
  jobs_failed INT UNSIGNED NOT NULL DEFAULT 0,
  places_skipped INT UNSIGNED NOT NULL DEFAULT 0,
  KEY ix_cycle_started (started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  source VARCHAR(60) NOT NULL,
  city VARCHAR(100) NOT NULL,
  state VARCHAR(30) NOT NULL,
  item_count INT NOT NULL DEFAULT 0,
  error_message VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_sync_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================
-- MIGRAÇÃO: 003_GOVERNANCE.SQL
-- ===========================================================
-- name: proveniência, moderação, mídia e auditoria

-- Ligação entre o estabelecimento e o ID na fonte, com instante da consulta e
-- nível de qualidade. Base do "origem e data de atualização" exibido ao usuário.
CREATE TABLE IF NOT EXISTS source_observations (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NULL,
  provider VARCHAR(40) NOT NULL,
  external_id VARCHAR(250) NOT NULL,
  source_url VARCHAR(350) NULL,
  first_seen_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  last_seen_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  last_fetched_at DATETIME(6) NULL,
  data_quality ENUM('unverified','corroborated','verified','conflicting') NOT NULL DEFAULT 'unverified',
  payload_hash CHAR(64) NULL,
  UNIQUE KEY uq_source (provider, external_id),
  KEY ix_observation_restaurant (restaurant_id),
  CONSTRAINT fk_observation_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS moderation_queue (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  item_type VARCHAR(30) NOT NULL,
  item_id BIGINT UNSIGNED NOT NULL,
  reason VARCHAR(40) NOT NULL,
  detail VARCHAR(500) NOT NULL,
  status ENUM('pending','approved','rejected') NOT NULL DEFAULT 'pending',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  reviewed_at DATETIME(6) NULL,
  reviewed_by VARCHAR(160) NULL,
  KEY ix_moderation_pending (status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Mídia só pode ser exibida com autorização e licença registradas.
CREATE TABLE IF NOT EXISTS media_assets (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  object_key VARCHAR(350) NOT NULL,
  alt_text VARCHAR(250) NOT NULL,
  license_type VARCHAR(80) NOT NULL,
  permission_reference VARCHAR(350) NOT NULL,
  attribution_text VARCHAR(350) NULL,
  moderation_status ENUM('pending','approved','rejected') NOT NULL DEFAULT 'pending',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_media_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id),
  KEY ix_media_restaurant (restaurant_id, moderation_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ratings (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_subject VARCHAR(160) NOT NULL,
  stars TINYINT UNSIGNED NOT NULL,
  review_text VARCHAR(2000) NULL,
  status ENUM('pending','approved','rejected') NOT NULL DEFAULT 'pending',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_rating_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id),
  UNIQUE KEY uq_rating_subject (restaurant_id, user_subject),
  CONSTRAINT ck_stars CHECK (stars BETWEEN 1 AND 5)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Trilha de auditoria imutável. Escrita na mesma transação da ação auditada.
CREATE TABLE IF NOT EXISTS audit_events (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  actor_subject VARCHAR(160) NULL,
  action VARCHAR(80) NOT NULL,
  entity_type VARCHAR(80) NOT NULL,
  entity_id BIGINT UNSIGNED NULL,
  metadata JSON NULL,
  ip_address VARCHAR(45) NULL,
  occurred_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_audit_entity (entity_type, entity_id),
  KEY ix_audit_occurred (occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Histórico de alterações de um estabelecimento, para o item 6 do painel.
CREATE TABLE IF NOT EXISTS change_history (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  field VARCHAR(60) NOT NULL,
  old_value VARCHAR(500) NULL,
  new_value VARCHAR(500) NULL,
  changed_by VARCHAR(160) NULL,
  changed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_change_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id),
  KEY ix_change_restaurant (restaurant_id, changed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Profiles sociais só existem com autorização do titular. Nunca inventados.
CREATE TABLE IF NOT EXISTS social_profiles (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  network ENUM('instagram','facebook','tiktok','youtube') NOT NULL,
  external_id VARCHAR(250) NOT NULL,
  profile_url VARCHAR(350) NOT NULL,
  authorization_reference VARCHAR(350) NOT NULL,
  authorized_at DATETIME(6) NOT NULL,
  revoked_at DATETIME(6) NULL,
  CONSTRAINT fk_social_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id),
  UNIQUE KEY uq_social_network_external (network, external_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================
-- MIGRAÇÃO: 004_IDENTITY_AND_COSTS.SQL
-- ===========================================================
-- name: identidade administrativa, MFA e controle de custo

CREATE TABLE IF NOT EXISTS admin_users (
  id VARCHAR(64) NOT NULL PRIMARY KEY,
  user_name VARCHAR(100) NOT NULL,
  email VARCHAR(200) NOT NULL,
  email_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
  password_hash VARCHAR(255) NULL,
  two_factor_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  -- Segredo TOTP guardado cifrado; nunca em texto puro.
  two_factor_secret VARBINARY(255) NULL,
  lockout_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  lockout_end_at DATETIME(6) NULL,
  access_failed_count INT UNSIGNED NOT NULL DEFAULT 0,
  last_login_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_admin_username (user_name),
  UNIQUE KEY uq_admin_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS admin_roles (
  id VARCHAR(64) NOT NULL PRIMARY KEY,
  name VARCHAR(64) NOT NULL,
  UNIQUE KEY uq_role_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS admin_user_roles (
  user_id VARCHAR(64) NOT NULL,
  role_id VARCHAR(64) NOT NULL,
  PRIMARY KEY (user_id, role_id),
  CONSTRAINT fk_user_role_user FOREIGN KEY (user_id) REFERENCES admin_users (id) ON DELETE CASCADE,
  CONSTRAINT fk_user_role_role FOREIGN KEY (role_id) REFERENCES admin_roles (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Chaves de segundo fator na recuperação, para o operador não perder o acesso.
CREATE TABLE IF NOT EXISTS admin_recovery_codes (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  user_id VARCHAR(64) NOT NULL,
  code_hash CHAR(64) NOT NULL,
  used_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_recovery_user FOREIGN KEY (user_id) REFERENCES admin_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Lançamento de custo por chamada. Valores em micros de USD (inteiro) para
-- não acumular erro de ponto flutuante em dinheiro.
CREATE TABLE IF NOT EXISTS ai_cost_entries (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  provider VARCHAR(40) NOT NULL,
  operation VARCHAR(80) NOT NULL,
  input_tokens INT UNSIGNED NOT NULL DEFAULT 0,
  output_tokens INT UNSIGNED NOT NULL DEFAULT 0,
  cost_micros_usd BIGINT UNSIGNED NOT NULL DEFAULT 0,
  success BOOLEAN NOT NULL,
  error_message VARCHAR(500) NULL,
  occurred_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_cost_period (provider, occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS api_budgets (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  provider VARCHAR(40) NOT NULL,
  monthly_limit_micros_usd BIGINT UNSIGNED NOT NULL,
  monthly_limit_requests INT UNSIGNED NOT NULL DEFAULT 0,
  paused BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  updated_by VARCHAR(160) NULL,
  UNIQUE KEY uq_budget_provider (provider)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Controle de taxa por API, para não estourar a cota do Google.
CREATE TABLE IF NOT EXISTS api_rate_limits (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  provider VARCHAR(40) NOT NULL,
  window_started_at DATETIME(6) NOT NULL,
  used_units INT UNSIGNED NOT NULL DEFAULT 0,
  limit_units INT UNSIGNED NOT NULL,
  UNIQUE KEY uq_rate_window (provider, window_started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Pausa global por integração, controlada pelo painel.
CREATE TABLE IF NOT EXISTS connector_states (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(60) NOT NULL,
  enabled BOOLEAN NOT NULL DEFAULT TRUE,
  last_success_at DATETIME(6) NULL,
  last_error VARCHAR(500) NULL,
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  updated_by VARCHAR(160) NULL,
  UNIQUE KEY uq_connector (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================
-- MIGRAÇÃO: 005_SEED_REFERENCE_DATA.SQL
-- ===========================================================
-- name: carga inicial de municípios, bairros e categorias

INSERT IGNORE INTO cities (name, normalized_name, state) VALUES
  ('Belém', 'belem', 'PA'),
  ('Ananindeua', 'ananindeua', 'PA'),
  ('Marabá', 'maraba', 'PA'),
  ('Santarém', 'santarem', 'PA'),
  ('Salinópolis', 'salinopolis', 'PA'),
  ('Castanhal', 'castanhal', 'PA'),
  ('Paragominas', 'paragominas', 'PA'),
  ('Canaã dos Carajás', 'canaa dos carajas', 'PA'),
  ('Barcarena', 'barcarena', 'PA'),
  ('Bragança', 'braganca', 'PA'),
  ('Abaetetuba', 'abaetetuba', 'PA'),
  ('Altamira', 'altamira', 'PA'),
  ('Tucuruí', 'tucurui', 'PA'),
  ('Parauapebas', 'parauapebas', 'PA'),
  ('Cametá', 'cameta', 'PA'),
  ('Benevides', 'benevides', 'PA'),
  ('Marituba', 'marituba', 'PA'),
  ('Santa Izabel do Pará', 'santa izabel do para', 'PA'),
  ('Vigia', 'vigia', 'PA'),
  ('Capanema', 'capanema', 'PA'),
  ('Soure', 'soure', 'PA'),
  ('Oeiras do Pará', 'oeiras do para', 'PA'),
  ('Portel', 'portel', 'PA'),
  ('Aveiro', 'aveiro', 'PA'),
  ('Óbidos', 'obidos', 'PA'),
  ('Itaituba', 'itaituba', 'PA');

INSERT IGNORE INTO categories (name, slug) VALUES
  ('Restaurante', 'restaurante'),
  ('Pizzaria', 'pizzaria'),
  ('Lanchonete', 'lanchonete'),
  ('Açaí', 'acai'),
  ('Cafeteria', 'cafeteria'),
  ('Tacacá', 'tacaca'),
  ('Churrascaria', 'churrascaria'),
  ('Bar', 'bar'),
  ('Buffet', 'buffet'),
  ('Padaria', 'padaria'),
  ('Hamburgueria', 'hamburgueria'),
  ('Pastelaria', 'pastelaria');

INSERT IGNORE INTO neighborhoods (city_id, name, normalized_name)
SELECT c.id, b.name, b.normalized_name
FROM cities c
JOIN (
  SELECT 'Belém' city, 'Nazaré' name, 'nazare' normalized_name UNION ALL
  SELECT 'Belém', 'Saúde', 'saude' UNION ALL
  SELECT 'Belém', 'Boa Vista', 'boa vista' UNION ALL
  SELECT 'Belém', 'Umaré', 'umare' UNION ALL
  SELECT 'Belém', 'Reduto', 'reduto' UNION ALL
  SELECT 'Belém', 'Jardim Salvador', 'jardim salvador' UNION ALL
  SELECT 'Belém', 'Ponta da Pedreira', 'ponta da pedreira' UNION ALL
  SELECT 'Belém', 'Batista Campos', 'batista campos' UNION ALL
  SELECT 'Belém', 'São Lourenço', 'sao lorenco' UNION ALL
  SELECT 'Belém', 'Água Branca', 'agua branca' UNION ALL
  SELECT 'Belém', 'Ipiranga', 'ipiranga' UNION ALL
  SELECT 'Belém', 'Guajará', 'guajara' UNION ALL
  SELECT 'Belém', 'Marco', 'marco' UNION ALL
  SELECT 'Belém', 'Montese', 'montese' UNION ALL
  SELECT 'Belém', 'Curió', 'curio' UNION ALL
  SELECT 'Belém', 'Tuna', 'tuna' UNION ALL
  SELECT 'Belém', 'Bengui', 'bengui' UNION ALL
  SELECT 'Ananindeua', 'Central', 'central' UNION ALL
  SELECT 'Ananindeua', 'Jardim Atlântico', 'jardim atlantico' UNION ALL
  SELECT 'Castanhal', 'Centro', 'centro' UNION ALL
  SELECT 'Santarém', 'Centro', 'centro' UNION ALL
  SELECT 'Santarém', 'Fatima', 'fatima'
) b ON b.city = c.name;

-- Alvos iniciais de descoberta. enabled=FALSE: o operador habilita depois de
-- validar credenciais e orçamento, evitando consumo no primeiro arranque.
INSERT IGNORE INTO discovery_targets (city, normalized_city, category, enabled)
SELECT t.city, t.normalized_city, t.category, FALSE
FROM (
  SELECT 'Belém' city, 'belem' normalized_city, 'restaurante' category UNION ALL
  SELECT 'Belém', 'belem', 'pizzaria' UNION ALL
  SELECT 'Belém', 'belem', 'lanchonete' UNION ALL
  SELECT 'Belém', 'belem', 'acai' UNION ALL
  SELECT 'Belém', 'belem', 'cafeteria' UNION ALL
  SELECT 'Belém', 'belem', 'tacaca' UNION ALL
  SELECT 'Ananindeua', 'ananindeua', 'restaurante' UNION ALL
  SELECT 'Ananindeua', 'ananindeua', 'pizzaria' UNION ALL
  SELECT 'Ananindeua', 'ananindeua', 'acai' UNION ALL
  SELECT 'Marabá', 'maraba', 'restaurante' UNION ALL
  SELECT 'Marabá', 'maraba', 'pizzaria' UNION ALL
  SELECT 'Santarém', 'santarem', 'restaurante' UNION ALL
  SELECT 'Santarém', 'santarem', 'tacaca' UNION ALL
  SELECT 'Santarém', 'santarem', 'acai' UNION ALL
  SELECT 'Castanhal', 'castanhal', 'restaurante' UNION ALL
  SELECT 'Castanhal', 'castanhal', 'pizzaria' UNION ALL
  SELECT 'Paragominas', 'paragominas', 'restaurante' UNION ALL
  SELECT 'Soure', 'soure', 'restaurante' UNION ALL
  SELECT 'Soure', 'soure', 'acai' UNION ALL
  SELECT 'Santarém', 'santarem', 'cafeteria' UNION ALL
  SELECT 'Marabá', 'maraba', 'acai' UNION ALL
  SELECT 'Parauapebas', 'parauapebas', 'restaurante' UNION ALL
  SELECT 'Tucuruí', 'tucurui', 'restaurante' UNION ALL
  SELECT 'Cametá', 'cameta', 'tacaca' UNION ALL
  SELECT 'Bragança', 'braganca', 'restaurante' UNION ALL
  SELECT 'Altamira', 'altamira', 'restaurante' UNION ALL
  SELECT 'Canaã dos Carajás', 'canaa dos carajas', 'restaurante' UNION ALL
  SELECT 'Marituba', 'marituba', 'restaurante' UNION ALL
  SELECT 'Benevides', 'benevides', 'restaurante' UNION ALL
  SELECT 'Santa Izabel do Pará', 'santa izabel do para', 'restaurante' UNION ALL
  SELECT 'Capanema', 'capanema', 'restaurante' UNION ALL
  SELECT 'Vigia', 'vigia', 'restaurante' UNION ALL
  SELECT 'Barcarena', 'barcarena', 'restaurante' UNION ALL
  SELECT 'Abaetetuba', 'abaetetuba', 'restaurante' UNION ALL
  SELECT 'Salinópolis', 'salinopolis', 'restaurante' UNION ALL
  SELECT 'Belém', 'belem', 'churrascaria' UNION ALL
  SELECT 'Belém', 'belem', 'pastelaria'
) t;

-- Papéis administrativos. O código recusa remover o último platform_admin.
INSERT IGNORE INTO admin_roles (id, name) VALUES
  (UUID(), 'restaurant_moderator'),
  (UUID(), 'platform_operator'),
  (UUID(), 'platform_admin');

INSERT IGNORE INTO api_budgets (provider, monthly_limit_micros_usd, monthly_limit_requests, paused) VALUES
  ('Gemini', 0, 0, TRUE),
  ('Groq', 0, 0, TRUE),
  ('DeepSeek', 0, 0, TRUE),
  ('GooglePlaces', 0, 0, TRUE);

-- ===========================================================
-- MIGRAÇÃO: 006_ADMIN_GOVERNANCE.SQL
-- ===========================================================
-- name: governança administrativa da Sprint 02

ALTER TABLE admin_users ADD COLUMN is_disabled BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE admin_users ADD COLUMN password_changed_at DATETIME(6) NULL;

-- (mantém coluna 'disabled' em bases antigas, renomeia se necessário é feito pelo operador)
ALTER TABLE admin_users ADD COLUMN disabled BOOLEAN NOT NULL DEFAULT FALSE AFTER lockout_end_at;
ALTER TABLE admin_users ADD COLUMN updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) AFTER created_at;

-- Papéis da jornada editorial e comercial.
INSERT INTO admin_roles (id, name) VALUES
  (UUID(), 'content_editor'),
  (UUID(), 'advertiser'),
  (UUID(), 'restaurant_moderator'),
  (UUID(), 'platform_operator'),
  (UUID(), 'platform_admin')
ON DUPLICATE KEY UPDATE name = VALUES(name);

ALTER TABLE audit_events ADD INDEX ix_audit_actor (actor_subject, occurred_at);

CREATE INDEX ix_recovery_user_unused
  ON admin_recovery_codes (user_id, used_at);

-- ===========================================================
-- MIGRAÇÃO: 007_FAVORITES_LGPD.SQL
-- ===========================================================
-- name: favoritos, avaliações autenticadas e pedidos LGPD

CREATE TABLE IF NOT EXISTS favorites (
  user_subject VARCHAR(160) NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_subject, restaurant_id),
  CONSTRAINT fk_favorite_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS deletion_requests (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  requester_subject VARCHAR(160) NOT NULL,
  target_type VARCHAR(40) NOT NULL,
  target_id BIGINT UNSIGNED NOT NULL,
  reason VARCHAR(500) NULL,
  status ENUM('pending','completed','rejected') NOT NULL DEFAULT 'pending',
  requested_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  KEY ix_deletion_pending (status, requested_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS consent_records (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  subject VARCHAR(160) NOT NULL,
  purpose VARCHAR(80) NOT NULL,
  granted BOOLEAN NOT NULL,
  granted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  revoked_at DATETIME(6) NULL,
  UNIQUE KEY uq_consent_subject_purpose (subject, purpose, granted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================
-- FIM DO SCRIPT belemrestaurante.sql
-- ===========================================================
