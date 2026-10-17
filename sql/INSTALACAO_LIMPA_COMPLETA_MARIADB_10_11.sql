-- BELEM RESTAURANTES - INSTALACAO LIMPA DO SCHEMA (MARIADB 10.11 / MYSQL 8)
-- ORIGEM: as 15 migrations em src/BelemRestaurantes.Infrastructure/Migrations/Scripts
-- ATENCAO: EXECUTAR SOMENTE EM BANCO VAZIO. FAZER BACKUP DO BANCO ATUAL!
-- ESTE SCRIPT NAO CONTEM DROP, TRUNCATE NEM RESET DE DADOS.
-- NUNCA EXECUTAR SOBRE belrestaurante PARCIALMENTE MIGRADO.
-- ALTERACOES DE ESQUEMA EM INNODB/MARIADB GERAM COMMIT IMPLICITO.
-- SE ALGUM COMANDO FALHAR, PARAR; NAO TENTAR CONTINUAR MANUALMENTE.
-- OS CHECKSUMS REGISTRADOS SAO OS SHA256 DOS SCRIPTS ORIGINAIS NO REPO APOS CORRECOES 011/013.

CREATE DATABASE IF NOT EXISTS belrestaurante CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE belrestaurante;

-- BLOQUEIO DE SEGURANCA: aborta se ja existir QUALQUER tabela no schema.
-- HeidiSQL: executar o arquivo inteiro (nao apenas uma selecao de comandos).
DELIMITER $
CREATE PROCEDURE belrestaurante_assert_empty_schema()
BEGIN
 IF (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='belrestaurante') > 0 THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='INSTALACAO CANCELADA: banco belrestaurante nao esta vazio. Faça backup e use um banco novo.';
 END IF;
END$
DELIMITER ;
CALL belrestaurante_assert_empty_schema();
DROP PROCEDURE belrestaurante_assert_empty_schema;

CREATE TABLE IF NOT EXISTS schema_migrations (
 id VARCHAR(64) NOT NULL PRIMARY KEY, name VARCHAR(160) NOT NULL,
 checksum CHAR(64) NOT NULL, applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS schema_migration_attempts (
 id VARCHAR(64) PRIMARY KEY,checksum CHAR(64) NOT NULL,
 started_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),completed_at DATETIME(6) NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ==================================================
-- MIGRACAO 001: 001_core_catalog.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('001','catálogos e estrutura base do guia','B2649F0058350BF99E981C23A7C473E4BE0FBD1D3733A1D6B92BB9D1D4751ED3',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('001','B2649F0058350BF99E981C23A7C473E4BE0FBD1D3733A1D6B92BB9D1D4751ED3',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 002: 002_discovery.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('002','fila persistente e alvos de descoberta','CD135AB05BAE092AC158B1048609529DA1CA0401DB97902B08FE47C51C589880',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('002','CD135AB05BAE092AC158B1048609529DA1CA0401DB97902B08FE47C51C589880',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 003: 003_governance.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('003','proveniência, moderação, mídia e auditoria','B4F251B1D8A93FCF1BABA227192A482D66AD0D24CDFB0DB5FF63FE799870DBE6',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('003','B4F251B1D8A93FCF1BABA227192A482D66AD0D24CDFB0DB5FF63FE799870DBE6',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 004: 004_identity_and_costs.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('004','identidade administrativa, MFA e controle de custo','3608D9F253BB9FAF85D08E3B8F3855E3D72228B50CFAE5B147E0411D1083F9CC',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('004','3608D9F253BB9FAF85D08E3B8F3855E3D72228B50CFAE5B147E0411D1083F9CC',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 005: 005_seed_reference_data.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('005','carga inicial de municípios, bairros e categorias','BDED586A04E10FA49346D10843B42A67E0FD896C67CA2D1C07E304711AC9C94D',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('005','BDED586A04E10FA49346D10843B42A67E0FD896C67CA2D1C07E304711AC9C94D',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 006: 006_admin_governance.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('006','governança administrativa da Sprint 02','8557F98D49B3DCDEA43C30FCC629881186DA66653B6EBFDF59AE9D9038041461',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('006','8557F98D49B3DCDEA43C30FCC629881186DA66653B6EBFDF59AE9D9038041461',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 007: 007_favorites_lgpd.sql
-- ==================================================
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

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('007','favoritos, avaliações autenticadas e pedidos LGPD','7E5D245A4B5C63A7A7E1ECFDC792C6C21BEE78FAE3D83FF3929CFDEBEA54BCA4',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('007','7E5D245A4B5C63A7A7E1ECFDC792C6C21BEE78FAE3D83FF3929CFDEBEA54BCA4',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 008: 008_mfa_replay.sql
-- ==================================================
-- name: proteção contra reutilização de códigos TOTP
ALTER TABLE admin_users ADD COLUMN last_totp_counter BIGINT NOT NULL DEFAULT -1;

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('008','proteção contra reutilização de códigos TOTP','7915CD3A84274F9640AC218D29BB20DD55276F6D8A84A7F8F7031157755F0788',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('008','7915CD3A84274F9640AC218D29BB20DD55276F6D8A84A7F8F7031157755F0788',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 009: 009_member_accounts.sql
-- ==================================================
-- name: contas da comunidade, sessões, tokens únicos e outbox privada
CREATE TABLE member_users (
 id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 full_name VARCHAR(160) NOT NULL,
 email VARCHAR(200) NOT NULL,
 normalized_email VARCHAR(200) NOT NULL,
 password_hash VARCHAR(255) NOT NULL,
 security_stamp CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 email_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
 is_blocked BOOLEAN NOT NULL DEFAULT FALSE,
 failed_attempts INT UNSIGNED NOT NULL DEFAULT 0,
 lockout_until DATETIME(6) NULL,
 preferred_culture VARCHAR(16) NOT NULL DEFAULT 'pt-BR',
 terms_version VARCHAR(40) NOT NULL,
 terms_accepted_at DATETIME(6) NOT NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_member_email(normalized_email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE member_sessions (
 id_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 security_stamp CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 revoked_at DATETIME(6) NULL,
 KEY ix_member_sessions(user_id, revoked_at, expires_at),
 CONSTRAINT fk_member_session FOREIGN KEY(user_id) REFERENCES member_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE member_tokens (
 token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 purpose ENUM('confirm','reset') NOT NULL,
 security_stamp CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 used_at DATETIME(6) NULL,
 KEY ix_member_tokens(user_id, purpose, created_at),
 CONSTRAINT fk_member_token FOREIGN KEY(user_id) REFERENCES member_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE transactional_email_outbox (
 id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 payload MEDIUMBLOB NULL,
 state ENUM('pending','sending','sent','dead_lettered') NOT NULL DEFAULT 'pending',
 attempts INT UNSIGNED NOT NULL DEFAULT 0,
 next_attempt_at DATETIME(6) NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 lease_id CHAR(32) NULL,
 locked_until DATETIME(6) NULL,
 last_error VARCHAR(80) NULL,
 completed_at DATETIME(6) NULL,
 KEY ix_email_claim(state,next_attempt_at,locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('009','contas da comunidade, sessões, tokens únicos e outbox privada','A2877D759308ABF0A74056CD266B02B3533166395FB5F62C347B1FD8BAE5CB96',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('009','A2877D759308ABF0A74056CD266B02B3533166395FB5F62C347B1FD8BAE5CB96',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 010: 010_experiences_photos_cuia.sql
-- ==================================================
-- name: experiencias comunitarias, fotos seguras, selo cuia e governanca lgpd

ALTER TABLE member_users
  ADD COLUMN email_change_new VARCHAR(200) NULL,
  ADD COLUMN email_change_token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
  ADD COLUMN email_change_expires_at DATETIME(6) NULL,
  ADD COLUMN bio VARCHAR(300) NULL,
  ADD COLUMN is_anonymized BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN anonymized_at DATETIME(6) NULL;

CREATE TABLE user_experiences (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  author_name VARCHAR(160) NOT NULL,
  stars TINYINT UNSIGNED NOT NULL,
  service_stars TINYINT UNSIGNED NOT NULL DEFAULT 5,
  flavor_stars TINYINT UNSIGNED NOT NULL DEFAULT 5,
  ambiance_stars TINYINT UNSIGNED NOT NULL DEFAULT 5,
  cost_benefit_stars TINYINT UNSIGNED NOT NULL DEFAULT 5,
  review_text TEXT NULL,
  dishes_consumed VARCHAR(500) NULL,
  price_paid DECIMAL(10,2) NULL,
  visibility ENUM('public','private','unlisted') NOT NULL DEFAULT 'public',
  unlisted_token CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
  status ENUM('draft','pending','approved','rejected','deleted') NOT NULL DEFAULT 'pending',
  rejection_reason VARCHAR(500) NULL,
  appeal_text VARCHAR(1000) NULL,
  appeal_status ENUM('none','pending','accepted','denied') NOT NULL DEFAULT 'none',
  appeal_reviewed_by VARCHAR(160) NULL,
  appeal_reviewed_at DATETIME(6) NULL,
  likes_count INT UNSIGNED NOT NULL DEFAULT 0,
  comments_count INT UNSIGNED NOT NULL DEFAULT 0,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  published_at DATETIME(6) NULL,
  KEY ix_exp_restaurant (restaurant_id, status, visibility),
  KEY ix_exp_user (user_id, status),
  KEY ix_exp_published (status, visibility, published_at),
  UNIQUE KEY uq_exp_unlisted (unlisted_token),
  CONSTRAINT fk_exp_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  CONSTRAINT fk_exp_user FOREIGN KEY (user_id) REFERENCES member_users (id) ON DELETE CASCADE,
  CONSTRAINT ck_exp_stars CHECK (stars BETWEEN 1 AND 5),
  CONSTRAINT ck_exp_service CHECK (service_stars BETWEEN 1 AND 5),
  CONSTRAINT ck_exp_flavor CHECK (flavor_stars BETWEEN 1 AND 5),
  CONSTRAINT ck_exp_ambiance CHECK (ambiance_stars BETWEEN 1 AND 5),
  CONSTRAINT ck_exp_cost CHECK (cost_benefit_stars BETWEEN 1 AND 5)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE experience_photos (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  experience_id BIGINT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  file_name VARCHAR(120) NOT NULL,
  thumbnail_name VARCHAR(120) NOT NULL,
  original_name VARCHAR(255) NOT NULL,
  content_type VARCHAR(60) NOT NULL,
  file_size INT UNSIGNED NOT NULL,
  width INT UNSIGNED NOT NULL,
  height INT UNSIGNED NOT NULL,
  caption VARCHAR(255) NULL,
  photo_type ENUM('community','official','illustrative') NOT NULL DEFAULT 'community',
  moderation_status ENUM('pending','approved','rejected') NOT NULL DEFAULT 'pending',
  moderated_by VARCHAR(160) NULL,
  moderated_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_photo_experience (experience_id, moderation_status),
  KEY ix_photo_restaurant (restaurant_id, photo_type, moderation_status),
  CONSTRAINT fk_photo_experience FOREIGN KEY (experience_id) REFERENCES user_experiences (id) ON DELETE CASCADE,
  CONSTRAINT fk_photo_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  CONSTRAINT fk_photo_user FOREIGN KEY (user_id) REFERENCES member_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE experience_likes (
  user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  experience_id BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_id, experience_id),
  CONSTRAINT fk_like_user FOREIGN KEY (user_id) REFERENCES member_users (id) ON DELETE CASCADE,
  CONSTRAINT fk_like_exp FOREIGN KEY (experience_id) REFERENCES user_experiences (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE experience_comments (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  experience_id BIGINT UNSIGNED NOT NULL,
  user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  author_name VARCHAR(160) NOT NULL,
  comment_text VARCHAR(1000) NOT NULL,
  is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_exp_comments (experience_id, is_deleted, created_at),
  CONSTRAINT fk_comment_exp FOREIGN KEY (experience_id) REFERENCES user_experiences (id) ON DELETE CASCADE,
  CONSTRAINT fk_comment_user FOREIGN KEY (user_id) REFERENCES member_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE community_reports (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  reporter_user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  target_type ENUM('experience','photo','comment') NOT NULL,
  target_id BIGINT UNSIGNED NOT NULL,
  reason ENUM('offensive','harassment','pii','fake','spam','other') NOT NULL,
  notes VARCHAR(500) NULL,
  status ENUM('pending','reviewed_upheld','reviewed_dismissed') NOT NULL DEFAULT 'pending',
  reviewed_by VARCHAR(160) NULL,
  reviewed_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_reports_pending (status, created_at),
  CONSTRAINT fk_report_user FOREIGN KEY (reporter_user_id) REFERENCES member_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE cuia_rankings (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  category_key VARCHAR(60) NOT NULL,
  rank_position INT UNSIGNED NOT NULL,
  score DECIMAL(3,2) NOT NULL,
  approved_reviews_count INT UNSIGNED NOT NULL,
  badge_title VARCHAR(120) NOT NULL,
  methodology_summary VARCHAR(500) NOT NULL,
  calculated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_cuia_cat_rank (category_key, rank_position),
  KEY ix_cuia_restaurant (restaurant_id),
  CONSTRAINT fk_cuia_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE lgpd_requests (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  request_type ENUM('export','deletion','anonymization','rectification') NOT NULL,
  status ENUM('pending','processing','completed','rejected') NOT NULL DEFAULT 'pending',
  payload_export MEDIUMTEXT NULL,
  notes VARCHAR(500) NULL,
  requested_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  KEY ix_lgpd_status (status, requested_at),
  CONSTRAINT fk_lgpd_user FOREIGN KEY (user_id) REFERENCES member_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('010','experiencias comunitarias, fotos seguras, selo cuia e governanca lgpd','7A4FD5C7DCF2F423521EB8C8E1F7CA71C6BBC1ECD7BF22A42804E0ED6B375713',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('010','7A4FD5C7DCF2F423521EB8C8E1F7CA71C6BBC1ECD7BF22A42804E0ED6B375713',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 011: 011_partner_portal_menu_monetization_ai.sql
-- ==================================================
-- name: portal do restaurante parceiro, cardapio digital inteligente, monetizacao e cuia ia
-- Requer MySQL 8.0+

-- 1. Solicitacoes de reivindicacao de estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_claim_requests (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  business_name VARCHAR(200) NOT NULL,
  tax_id VARCHAR(30) NULL, -- CNPJ ou documento de identificacao
  contact_phone VARCHAR(50) NOT NULL,
  contact_email VARCHAR(180) NOT NULL,
  official_channels VARCHAR(500) NULL,
  verification_evidence TEXT NOT NULL,
  status ENUM('pending', 'approved', 'rejected') NOT NULL DEFAULT 'pending',
  rejection_reason VARCHAR(500) NULL,
  reviewed_by VARCHAR(64) NULL,
  reviewed_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  KEY ix_claim_restaurant (restaurant_id),
  KEY ix_claim_user (user_id),
  KEY ix_claim_status (status),
  CONSTRAINT fk_claim_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Vinculo ativo entre comerciantes e estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_owners (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  role VARCHAR(30) NOT NULL DEFAULT 'owner', -- owner, manager
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_owner_restaurant_user (restaurant_id, user_id),
  KEY ix_owner_user (user_id),
  CONSTRAINT fk_owner_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Perfil comercial estendido do restaurante parceiro
CREATE TABLE IF NOT EXISTS restaurant_profiles (
  restaurant_id BIGINT UNSIGNED PRIMARY KEY,
  official_description TEXT NULL,
  slogan VARCHAR(250) NULL,
  history TEXT NULL,
  commercial_phone VARCHAR(50) NULL,
  commercial_whatsapp VARCHAR(50) NULL,
  commercial_email VARCHAR(180) NULL,
  delivery_whatsapp VARCHAR(50) NULL,
  delivery_ifood_url VARCHAR(350) NULL,
  delivery_custom_url VARCHAR(350) NULL,
  is_accepting_orders BOOLEAN NOT NULL DEFAULT TRUE,
  has_wifi BOOLEAN NOT NULL DEFAULT FALSE,
  has_parking BOOLEAN NOT NULL DEFAULT FALSE,
  has_outdoor_seating BOOLEAN NOT NULL DEFAULT FALSE,
  is_pet_friendly BOOLEAN NOT NULL DEFAULT FALSE,
  is_accessible BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_profile_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Horarios regulares de funcionamento (0=Domingo, 1=Segunda, ..., 6=Sabado)
CREATE TABLE IF NOT EXISTS restaurant_schedules (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  day_of_week TINYINT UNSIGNED NOT NULL, -- 0 a 6
  is_closed BOOLEAN NOT NULL DEFAULT FALSE,
  open_time TIME NULL,
  close_time TIME NULL,
  UNIQUE KEY uq_schedule_day (restaurant_id, day_of_week),
  CONSTRAINT fk_schedule_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5. Horarios especiais e feriados (Cirio, Natal, feriados municipais paraenses)
CREATE TABLE IF NOT EXISTS restaurant_special_schedules (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  specific_date DATE NOT NULL,
  is_closed BOOLEAN NOT NULL DEFAULT FALSE,
  open_time TIME NULL,
  close_time TIME NULL,
  reason VARCHAR(150) NOT NULL,
  UNIQUE KEY uq_special_date (restaurant_id, specific_date),
  CONSTRAINT fk_special_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 6. Categorias do cardapio digital
CREATE TABLE IF NOT EXISTS menu_categories (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  name VARCHAR(100) NOT NULL,
  display_order INT NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  KEY ix_menu_cat_restaurant (restaurant_id, display_order),
  CONSTRAINT fk_menucat_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 7. Itens do cardapio digital inteligente
CREATE TABLE IF NOT EXISTS menu_items (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  category_id BIGINT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  name VARCHAR(150) NOT NULL,
  description TEXT NULL,
  price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  image_url VARCHAR(350) NULL,
  ingredients TEXT NULL,
  allergens VARCHAR(300) NULL,
  is_available BOOLEAN NOT NULL DEFAULT TRUE,
  is_regional_dish BOOLEAN NOT NULL DEFAULT FALSE,
  regional_dish_type VARCHAR(50) NULL, -- tacaca, manicoba, pato_tucupi, acai, vatapa, caruru, peixe_regional, sobremesa_paraense, outro
  display_order INT NOT NULL DEFAULT 0,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  KEY ix_menuitem_cat (category_id, display_order),
  KEY ix_menuitem_restaurant (restaurant_id),
  CONSTRAINT fk_menuitem_cat FOREIGN KEY (category_id) REFERENCES menu_categories (id) ON DELETE CASCADE,
  CONSTRAINT fk_menuitem_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 8. Eventos e novidades publicados pelo parceiro
CREATE TABLE IF NOT EXISTS restaurant_announcements (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  title VARCHAR(180) NOT NULL,
  description TEXT NOT NULL,
  image_url VARCHAR(350) NULL,
  event_date DATETIME NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_announcement_restaurant (restaurant_id, created_at),
  CONSTRAINT fk_announcement_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 9. Respostas oficiais dos estabelecimentos as avaliacoes dos clientes
CREATE TABLE IF NOT EXISTS restaurant_review_replies (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  experience_id BIGINT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  reply_text TEXT NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_reply_experience (experience_id),
  KEY ix_reply_restaurant (restaurant_id),
  CONSTRAINT fk_reply_experience FOREIGN KEY (experience_id) REFERENCES user_experiences (id) ON DELETE CASCADE,
  CONSTRAINT fk_reply_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 10. Metricas e estatisticas de visitas e engajamento
CREATE TABLE IF NOT EXISTS restaurant_stats (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  stat_date DATE NOT NULL,
  page_views INT UNSIGNED NOT NULL DEFAULT 0,
  whatsapp_clicks INT UNSIGNED NOT NULL DEFAULT 0,
  route_clicks INT UNSIGNED NOT NULL DEFAULT 0,
  menu_views INT UNSIGNED NOT NULL DEFAULT 0,
  UNIQUE KEY uq_stat_restaurant_date (restaurant_id, stat_date),
  CONSTRAINT fk_stats_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 11. Redes sociais oficiais e midias incorporadas autorizadas
CREATE TABLE IF NOT EXISTS restaurant_social_media (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  platform VARCHAR(30) NOT NULL, -- instagram, facebook, tiktok, youtube
  profile_url VARCHAR(350) NOT NULL,
  embed_code TEXT NULL,
  is_verified BOOLEAN NOT NULL DEFAULT FALSE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_social_restaurant (restaurant_id),
  CONSTRAINT fk_restaurant_social_media_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 12. Planos comerciais e monetizacao
CREATE TABLE IF NOT EXISTS commercial_plans (
  code VARCHAR(50) PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  description VARCHAR(350) NOT NULL,
  monthly_price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  badge_highlight BOOLEAN NOT NULL DEFAULT FALSE,
  menu_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  priority_support BOOLEAN NOT NULL DEFAULT FALSE,
  sponsored_exposure BOOLEAN NOT NULL DEFAULT FALSE,
  multi_branch BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Inserir planos padrao se nao existirem
INSERT IGNORE INTO commercial_plans (code, name, description, monthly_price, badge_highlight, menu_enabled, priority_support, sponsored_exposure, multi_branch, is_active)
VALUES
('free', 'Plano Gratuito', 'Perfil basico, localizacao, horarios regulares e avaliacoes comunitarias.', 0.00, FALSE, TRUE, FALSE, FALSE, FALSE, TRUE),
('cuia_pro', 'Plano Cuia Pro', 'Cardapio digital ilimitado, galeria ampliada, estatisticas de visitas e gestao de perfil.', 79.90, TRUE, TRUE, TRUE, FALSE, FALSE, TRUE),
('cuia_highlight', 'Plano Cuia Destaque', 'Todos os recursos Pro mais destaque editorial, campanhas patrocinadas identificadas e maior exposicao.', 149.90, TRUE, TRUE, TRUE, TRUE, FALSE, TRUE),
('cuia_networks', 'Plano Cuia Redes', 'Gestao unificada para redes e franquias com multiplos estabelecimentos e relatorios consolidados.', 299.90, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE);

-- Assinaturas ativas dos estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_subscriptions (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  plan_code VARCHAR(50) NOT NULL,
  status ENUM('active', 'pending', 'canceled', 'expired') NOT NULL DEFAULT 'active',
  started_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  expires_at DATETIME(6) NULL,
  payment_method VARCHAR(50) NOT NULL DEFAULT 'pix',
  KEY ix_sub_restaurant (restaurant_id),
  KEY ix_sub_user (user_id),
  CONSTRAINT fk_sub_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  CONSTRAINT fk_sub_plan FOREIGN KEY (plan_code) REFERENCES commercial_plans (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Pedidos e pagamentos dos comerciantes (com chave PIX MNSOFT)
CREATE TABLE IF NOT EXISTS merchant_orders (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  order_number VARCHAR(64) NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  plan_code VARCHAR(50) NOT NULL,
  amount DECIMAL(10,2) NOT NULL,
  payment_status ENUM('pending', 'paid', 'failed', 'refunded') NOT NULL DEFAULT 'pending',
  payment_method VARCHAR(50) NOT NULL DEFAULT 'pix',
  pix_cnpj VARCHAR(30) NOT NULL DEFAULT '18.160.057/0001-13',
  pix_copy_paste TEXT NULL,
  paid_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_order_number (order_number),
  KEY ix_order_restaurant (restaurant_id),
  CONSTRAINT fk_order_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Cupons de desconto e beneficios
CREATE TABLE IF NOT EXISTS restaurant_coupons (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  code VARCHAR(50) NOT NULL,
  title VARCHAR(150) NOT NULL,
  description VARCHAR(350) NOT NULL,
  discount_percentage TINYINT UNSIGNED NULL,
  discount_amount DECIMAL(10,2) NULL,
  valid_until DATE NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_coupon_restaurant (restaurant_id),
  CONSTRAINT fk_coupon_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 13. Mobilidade e Roteiros Gastronomicos do Para
CREATE TABLE IF NOT EXISTS gastronomic_trails (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  slug VARCHAR(80) NOT NULL UNIQUE,
  title VARCHAR(150) NOT NULL,
  region VARCHAR(100) NOT NULL, -- Belem, Ilha do Combu, Icoaraci, Mosqueiro, Salinopolis, Santarem, etc.
  description TEXT NOT NULL,
  cover_image VARCHAR(350) NULL,
  estimated_duration_hours INT NOT NULL DEFAULT 4,
  transport_tips TEXT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Paradas dos roteiros gastronômicos
CREATE TABLE IF NOT EXISTS trail_stops (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  trail_id INT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  stop_order INT NOT NULL DEFAULT 1,
  highlight_dish VARCHAR(150) NULL,
  notes VARCHAR(350) NULL,
  UNIQUE KEY uq_trail_stop (trail_id, restaurant_id),
  KEY ix_trail_stop_order (trail_id, stop_order),
  CONSTRAINT fk_stop_trail FOREIGN KEY (trail_id) REFERENCES gastronomic_trails (id) ON DELETE CASCADE,
  CONSTRAINT fk_stop_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 14. Alertas do Cuia Discovery para revisao administrativa
CREATE TABLE IF NOT EXISTS discovery_alerts (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NULL,
  external_id VARCHAR(255) NULL,
  alert_type ENUM('closed_down', 'address_changed', 'schedule_changed', 'conflict_detected', 'name_changed') NOT NULL,
  details JSON NOT NULL,
  status ENUM('pending', 'reviewed', 'dismissed') NOT NULL DEFAULT 'pending',
  reviewed_by VARCHAR(64) NULL,
  reviewed_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_alert_status (status),
  KEY ix_alert_restaurant (restaurant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('011','portal do restaurante parceiro, cardapio digital inteligente, monetizacao e cuia ia','C3518A191AEDF492FD1485A439DB68A21910A80271300668DDCEA0AE0CD1CCFF',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('011','C3518A191AEDF492FD1485A439DB68A21910A80271300668DDCEA0AE0CD1CCFF',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 012: 012_cuia_crawler.sql
-- ==================================================
-- name: cuia crawler - pesquisa autonoma na internet e descoberta gastronomica
-- Requer MySQL 8.0+

-- 1. Fontes autorizadas de rastreamento
CREATE TABLE IF NOT EXISTS crawler_sources (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(150) NOT NULL,
  base_url VARCHAR(500) NOT NULL,
  source_type ENUM('sitemap_xml', 'rss_feed', 'html_directory', 'structured_data') NOT NULL DEFAULT 'sitemap_xml',
  target_city VARCHAR(100) NULL,
  default_category VARCHAR(100) NULL,
  status ENUM('active', 'paused', 'blocked', 'pending_review') NOT NULL DEFAULT 'active',
  robots_txt_status ENUM('allowed', 'disallowed', 'unknown') NOT NULL DEFAULT 'unknown',
  crawl_delay_seconds INT UNSIGNED NOT NULL DEFAULT 5,
  last_crawled_at DATETIME(6) NULL,
  last_success_at DATETIME(6) NULL,
  last_error VARCHAR(500) NULL,
  etag VARCHAR(100) NULL,
  last_modified_header VARCHAR(100) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_crawler_source_url (base_url),
  KEY ix_crawler_source_status (status, last_crawled_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Fila persistente de URLs para rastreamento
CREATE TABLE IF NOT EXISTS crawler_tasks (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  source_id BIGINT UNSIGNED NOT NULL,
  url VARCHAR(500) NOT NULL,
  city VARCHAR(100) NOT NULL DEFAULT 'Belém',
  category VARCHAR(100) NULL,
  state ENUM('pending', 'running', 'succeeded', 'failed', 'skipped_robots') NOT NULL DEFAULT 'pending',
  depth INT UNSIGNED NOT NULL DEFAULT 0,
  attempts INT UNSIGNED NOT NULL DEFAULT 0,
  next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  locked_by VARCHAR(80) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  CONSTRAINT fk_crawler_task_source FOREIGN KEY (source_id) REFERENCES crawler_sources (id) ON DELETE CASCADE,
  UNIQUE KEY uq_crawler_task_url (url),
  KEY ix_crawler_task_claim (state, next_attempt_at, locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Telemetria e auditoria de execucoes de rastreamento
CREATE TABLE IF NOT EXISTS crawler_runs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  source_id BIGINT UNSIGNED NOT NULL,
  url VARCHAR(500) NOT NULL,
  status_code INT NOT NULL DEFAULT 0,
  duration_ms INT UNSIGNED NOT NULL DEFAULT 0,
  items_discovered INT UNSIGNED NOT NULL DEFAULT 0,
  items_ingested INT UNSIGNED NOT NULL DEFAULT 0,
  error_message VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_crawler_run_source FOREIGN KEY (source_id) REFERENCES crawler_sources (id) ON DELETE CASCADE,
  KEY ix_crawler_run_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Insercao de fontes autorizadas iniciais para pesquisa gastronomica no Para
INSERT IGNORE INTO crawler_sources (name, base_url, source_type, target_city, default_category, status, crawl_delay_seconds)
VALUES 
  ('Guia Gastronômico de Belém - Diretório Oficial', 'https://belemrestaurantes.com.br/sitemap.xml', 'sitemap_xml', 'Belém', 'Regional', 'active', 5),
  ('Polo Turístico e Gastronômico do Tapajós', 'https://turismo.santarem.pa.gov.br/gastronomia/rss', 'rss_feed', 'Santarém', 'Peixaria', 'active', 5),
  ('Circuito Gastronômico de Castanhal', 'https://castanhal.pa.gov.br/sitemap-gastronomia.xml', 'sitemap_xml', 'Castanhal', 'Regional', 'active', 5),
  ('Roteiro da Culinária Marajoara (Soure e Salvaterra)', 'https://marajo.tur.br/onde-comer/sitemap.xml', 'sitemap_xml', 'Soure', 'Regional', 'active', 5);

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('012','cuia crawler - pesquisa autonoma na internet e descoberta gastronomica','8DC1D0787D4F8A8C48C62CE978B909D457D4352B2F7D3AE457C0B5F65F148E7B',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('012','8DC1D0787D4F8A8C48C62CE978B909D457D4352B2F7D3AE457C0B5F65F148E7B',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 013: 013_cuia_discovery_360.sql
-- ==================================================
-- name: cuia discovery 360 - cobertura estadual, inteligencia de dados, enriquecimento e governanca
-- Requer MySQL 8.0+

-- 1. Cadastro Geografico Oficial dos Municipios e Regioes do Para
CREATE TABLE IF NOT EXISTS geo_municipalities (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  ibge_code INT UNSIGNED NOT NULL UNIQUE,
  name VARCHAR(100) NOT NULL,
  normalized_name VARCHAR(100) NOT NULL,
  mesoregion VARCHAR(80) NOT NULL,
  microregion VARCHAR(80) NOT NULL,
  is_tourist_pole BOOLEAN NOT NULL DEFAULT FALSE,
  priority_level INT NOT NULL DEFAULT 1, -- 1: RMB/Polos Turisticos, 2: Cidades polo, 3: Interior
  last_scanned_at DATETIME(6) NULL,
  coverage_status ENUM('unscanned', 'in_progress', 'covered', 'stale') NOT NULL DEFAULT 'unscanned',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_geo_meso (mesoregion),
  KEY ix_geo_priority (priority_level, last_scanned_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Fila especifica de enriquecimento continuo de estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_enrichment_queue (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  priority INT NOT NULL DEFAULT 1,
  state ENUM('pending', 'running', 'completed', 'failed') NOT NULL DEFAULT 'pending',
  fields_to_enrich VARCHAR(255) NOT NULL DEFAULT 'all',
  attempts INT NOT NULL DEFAULT 0,
  next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  locked_by VARCHAR(80) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  CONSTRAINT fk_enrich_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  KEY ix_enrich_claim (state, next_attempt_at, locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Rastreabilidade e historico de atributos de estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_attribute_history (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  attribute_name VARCHAR(60) NOT NULL,
  old_value TEXT NULL,
  new_value TEXT NOT NULL,
  source_type VARCHAR(60) NOT NULL, -- verified_owner, crawler_schema, crawler_html, google_places, community
  source_reference VARCHAR(500) NULL,
  confidence_score DECIMAL(4,3) NOT NULL DEFAULT 0.800,
  requires_moderation BOOLEAN NOT NULL DEFAULT FALSE,
  is_applied BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_attr_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  KEY ix_attr_restaurant_time (restaurant_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Motor de Fotografias, Videos e Midias com Atribuicao e Niveis de Prioridade
CREATE TABLE IF NOT EXISTS restaurant_media (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  media_type ENUM('photo_dish', 'photo_ambiance', 'photo_facade', 'video_embed') NOT NULL DEFAULT 'photo_dish',
  media_url VARCHAR(500) NOT NULL,
  thumbnail_url VARCHAR(500) NULL,
  embed_html TEXT NULL,
  priority_tier INT NOT NULL DEFAULT 3, -- 1: verified_owner, 2: community_approved, 3: licensed_external, 4: illustration
  source_origin VARCHAR(255) NOT NULL,
  attribution_text VARCHAR(255) NOT NULL,
  license_type VARCHAR(80) NOT NULL DEFAULT 'with_permission',
  status ENUM('pending', 'approved', 'rejected') NOT NULL DEFAULT 'pending',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_restaurant_media_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  KEY ix_media_restaurant_priority (restaurant_id, priority_tier, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5. Seed de Municipios Oficiais com Codigo IBGE do Estado do Para
INSERT IGNORE INTO geo_municipalities (ibge_code, name, normalized_name, mesoregion, microregion, is_tourist_pole, priority_level)
VALUES
  -- Metropolitana de Belém
  (1501402, 'Belém', 'belem', 'Metropolitana de Belém', 'Belém', TRUE, 1),
  (1500800, 'Ananindeua', 'ananindeua', 'Metropolitana de Belém', 'Belém', TRUE, 1),
  (1504422, 'Marituba', 'marituba', 'Metropolitana de Belém', 'Belém', FALSE, 1),
  (1501501, 'Benevides', 'benevides', 'Metropolitana de Belém', 'Belém', FALSE, 2),
  (1506544, 'Santa Bárbara do Pará', 'santa barbara do para', 'Metropolitana de Belém', 'Belém', FALSE, 2),
  (1506353, 'Santa Izabel do Pará', 'santa izabel do para', 'Metropolitana de Belém', 'Castanhal', FALSE, 2),
  (1502400, 'Castanhal', 'castanhal', 'Metropolitana de Belém', 'Castanhal', TRUE, 1),
  (1501303, 'Barcarena', 'barcarena', 'Metropolitana de Belém', 'Cametá', TRUE, 1),

  -- Baixo Amazonas / Tapajós
  (1506807, 'Santarém', 'santarem', 'Baixo Amazonas', 'Santarém', TRUE, 1),
  (1505106, 'Óbidos', 'obidos', 'Baixo Amazonas', 'Óbidos', TRUE, 2),
  (1505304, 'Oriximiná', 'oriximina', 'Baixo Amazonas', 'Óbidos', FALSE, 2),
  (1500404, 'Alenquer', 'alenquer', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1504802, 'Monte Alegre', 'monte alegre', 'Baixo Amazonas', 'Santarém', TRUE, 2),
  (1503002, 'Faro', 'faro', 'Baixo Amazonas', 'Óbidos', FALSE, 3),
  (1503754, 'Juruti', 'juruti', 'Baixo Amazonas', 'Óbidos', FALSE, 2),

  -- Marajó
  (1507953, 'Soure', 'soure', 'Marajó', 'Arari', TRUE, 1),
  (1506304, 'Salvaterra', 'salvaterra', 'Marajó', 'Arari', TRUE, 1),
  (1502806, 'Cachoeira do Arari', 'cachoeira do arari', 'Marajó', 'Arari', FALSE, 2),
  (1507706, 'São Sebastião da Boa Vista', 'sao sebastiao da boa vista', 'Marajó', 'Furos de Breves', FALSE, 3),
  (1501808, 'Breves', 'breves', 'Marajó', 'Furos de Breves', TRUE, 2),
  (1500701, 'Anajás', 'anajas', 'Marajó', 'Furos de Breves', FALSE, 3),
  (1505809, 'Portel', 'portel', 'Marajó', 'Portel', FALSE, 2),
  (1504901, 'Muaná', 'muana', 'Marajó', 'Arari', FALSE, 2),
  (1505908, 'Ponta de Pedras', 'ponta de pedras', 'Marajó', 'Arari', FALSE, 2),

  -- Nordeste Paraense / Salgado / Caetés
  (1506500, 'Salinópolis', 'salinopolis', 'Nordeste Paraense', 'Salgado', TRUE, 1),
  (1501709, 'Bragança', 'braganca', 'Nordeste Paraense', 'Bragantina', TRUE, 1),
  (1508407, 'Vigia', 'vigia', 'Nordeste Paraense', 'Salgado', TRUE, 1),
  (1502707, 'Cametá', 'cameta', 'Nordeste Paraense', 'Cametá', TRUE, 1),
  (1500107, 'Abaetetuba', 'abaetetuba', 'Nordeste Paraense', 'Cametá', TRUE, 1),
  (1505502, 'Paragominas', 'paragominas', 'Nordeste Paraense', 'Paragominas', TRUE, 1),
  (1502905, 'Capanema', 'capanema', 'Nordeste Paraense', 'Bragantina', TRUE, 2),
  (1502202, 'Capitão Poço', 'capitao poco', 'Nordeste Paraense', 'Guajarina', FALSE, 2),
  (1506585, 'Santa Maria do Pará', 'santa maria do para', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1504109, 'Moju', 'moju', 'Nordeste Paraense', 'Tomé-Açu', FALSE, 2),
  (1508001, 'Tomé-Açu', 'tome-acu', 'Nordeste Paraense', 'Tomé-Açu', TRUE, 2),

  -- Sudeste Paraense
  (1504208, 'Marabá', 'maraba', 'Sudeste Paraense', 'Marabá', TRUE, 1),
  (1505536, 'Parauapebas', 'parauapebas', 'Sudeste Paraense', 'Parauapebas', TRUE, 1),
  (1506138, 'Redenção', 'redencao', 'Sudeste Paraense', 'Redenção', TRUE, 2),
  (1508100, 'Tucuruí', 'tucurui', 'Sudeste Paraense', 'Tucuruí', TRUE, 2),
  (1502152, 'Canaã dos Carajás', 'canaa dos carajas', 'Sudeste Paraense', 'Parauapebas', TRUE, 2),
  (1508308, 'Xinguara', 'xinguara', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1507300, 'São Félix do Xingu', 'sao felix do xingu', 'Sudeste Paraense', 'São Félix do Xingu', FALSE, 2),

  -- Sudoeste Paraense
  (1500602, 'Altamira', 'altamira', 'Sudoeste Paraense', 'Altamira', TRUE, 1),
  (1503606, 'Itaituba', 'itaituba', 'Sudoeste Paraense', 'Itaituba', TRUE, 2),
  (1508084, 'Novo Progresso', 'novo progresso', 'Sudoeste Paraense', 'Itaituba', FALSE, 3);

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('013','cuia discovery 360 - cobertura estadual, inteligencia de dados, enriquecimento e governanca','073E57DE5475C45B4E5E2327D71CB28284BA48AEBE3442EC282A9DA8386C06A5',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('013','073E57DE5475C45B4E5E2327D71CB28284BA48AEBE3442EC282A9DA8386C06A5',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 014: 014_operacao_gastronomica_360.sql
-- ==================================================
-- name: operacao gastronomica 360 - inteligencia de catalogo, territorios, monetizacao e seguranca
-- Requer MySQL 8.0+

-- 1. Cadastro Estruturado de Distritos, Ilhas e Localidades Gastronômicas
CREATE TABLE IF NOT EXISTS geo_districts_localities (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  municipality_ibge INT UNSIGNED NOT NULL,
  name VARCHAR(100) NOT NULL,
  normalized_name VARCHAR(100) NOT NULL,
  locality_type ENUM('district', 'island', 'locality', 'tourist_zone') NOT NULL DEFAULT 'locality',
  is_tourist_highlight BOOLEAN NOT NULL DEFAULT FALSE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_locality_municipality FOREIGN KEY (municipality_ibge) REFERENCES geo_municipalities (ibge_code) ON DELETE CASCADE,
  KEY ix_loc_muni_type (municipality_ibge, locality_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Evolução de restaurantes com status operacional e validação de encerramento
ALTER TABLE restaurants
  ADD COLUMN IF NOT EXISTS district_locality_id INT UNSIGNED NULL,
  ADD COLUMN IF NOT EXISTS business_status ENUM('operational', 'temporarily_closed', 'permanently_closed') NOT NULL DEFAULT 'operational',
  ADD COLUMN IF NOT EXISTS last_activity_verified_at DATETIME(6) NULL;

-- 3. Fila Central do Cuia Discovery Intelligence (8 Tipos de Tarefas Unificadas)
CREATE TABLE IF NOT EXISTS discovery_intelligence_jobs (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  task_type ENUM(
    'new_discovery',
    'update_existing',
    'enrichment',
    'source_verification',
    'deduplication',
    'closure_validation',
    'media_review',
    'conflict_resolution'
  ) NOT NULL,
  restaurant_id BIGINT UNSIGNED NULL,
  source_provider VARCHAR(60) NOT NULL,
  priority INT NOT NULL DEFAULT 1,
  status ENUM('pending', 'running', 'completed', 'failed', 'dead_letter') NOT NULL DEFAULT 'pending',
  payload_json TEXT NULL,
  attempts INT NOT NULL DEFAULT 0,
  max_attempts INT NOT NULL DEFAULT 4,
  next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  locked_until DATETIME(6) NULL,
  locked_by VARCHAR(80) NULL,
  last_error VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  completed_at DATETIME(6) NULL,
  CONSTRAINT fk_dij_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  KEY ix_dij_claim (status, next_attempt_at, locked_until, priority)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Monetização Operacional: Faturas e Pagamentos PIX Autenticados
CREATE TABLE IF NOT EXISTS commercial_invoices (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  claim_id BIGINT UNSIGNED NULL,
  plan_code VARCHAR(40) NOT NULL DEFAULT 'destaque', -- basic, destaque, premium
  amount_cents INT UNSIGNED NOT NULL,
  pix_txid VARCHAR(100) NOT NULL UNIQUE,
  pix_copy_paste TEXT NOT NULL,
  pix_status ENUM('pending', 'paid', 'expired', 'cancelled') NOT NULL DEFAULT 'pending',
  auth_hash VARCHAR(128) NOT NULL,
  paid_at DATETIME(6) NULL,
  expires_at DATETIME(6) NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_inv_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  KEY ix_inv_status (pix_status, expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5. Seed dos Principais Distritos, Ilhas e Polos Gastronômicos Paraenses
INSERT IGNORE INTO geo_districts_localities (municipality_ibge, name, normalized_name, locality_type, is_tourist_highlight)
VALUES
  -- Belém (1501402)
  (1501402, 'Icoaraci (Vila Sorriso)', 'icoaraci', 'district', TRUE),
  (1501402, 'Ilha do Mosqueiro', 'mosqueiro', 'island', TRUE),
  (1501402, 'Ilha de Outeiro (Carananduba)', 'outeiro', 'island', TRUE),
  (1501402, 'Ilha do Combu (Gastronomia Fluvial)', 'combu', 'island', TRUE),
  (1501402, 'Ilha de Cotijuba', 'cotijuba', 'island', TRUE),
  (1501402, 'Campina / Comércio Histórico', 'campina', 'locality', TRUE),
  (1501402, 'Cidade Velha / Complexo Feliz Lusitânia', 'cidade velha', 'locality', TRUE),
  (1501402, 'Umarizal Gastronômico', 'umarizal', 'locality', TRUE),
  (1501402, 'Nazaré / Praça Batista Campos', 'nazare', 'locality', TRUE),

  -- Santarém (1506807)
  (1506807, 'Alter do Chão (Caribe Amazônico)', 'alter do chao', 'district', TRUE),
  (1506807, 'Orla do Tapajós', 'orla do tapajos', 'locality', TRUE),
  (1506807, 'Ponta de Pedras (Praia Tapajônica)', 'ponta de pedras tapajos', 'locality', TRUE),

  -- Marajó / Soure (1507953)
  (1507953, 'Praia do Pesqueiro (Culinária Marajoara)', 'praia do pesqueiro', 'tourist_zone', TRUE),
  (1507953, 'Praia da Barra Velha', 'praia da barra velha', 'tourist_zone', TRUE),

  -- Salinópolis (1506500)
  (1506500, 'Praia do Atalaia', 'praia do atalaia', 'tourist_zone', TRUE),
  (1506500, 'Praia do Farol Velho', 'praia do farol velho', 'tourist_zone', TRUE),

  -- Bragança (1501709)
  (1501709, 'Praia de Ajuruteua', 'praia de ajuruteua', 'tourist_zone', TRUE),
  (1501709, 'Vila dos Pescadores', 'vila dos pescadores', 'locality', FALSE),

  -- Barcarena (1501303)
  (1501303, 'Praia do Caripi', 'praia do caripi', 'tourist_zone', TRUE),
  (1501303, 'Vila dos Cabanos', 'vila dos cabanos', 'district', TRUE);

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('014','operacao gastronomica 360 - inteligencia de catalogo, territorios, monetizacao e seguranca','9A54714181B74DAEC59E9E33082EF32881989C88DB14629D1BA26A76AB1621C8',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('014','9A54714181B74DAEC59E9E33082EF32881989C88DB14629D1BA26A76AB1621C8',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- ==================================================
-- MIGRACAO 015: 015_cobertura_estadual_144_municipios.sql
-- ==================================================
-- name: cobertura estadual integral 144 municipios do para, telemetria e completude de catalogo
-- Requer MySQL 8.0+

-- 1. Evolução da tabela restaurants com contatos, horários e completude de perfil (FASE 4)
ALTER TABLE restaurants
  ADD COLUMN IF NOT EXISTS phone VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS whatsapp VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS opening_hours VARCHAR(500) NULL,
  ADD COLUMN IF NOT EXISTS profile_completeness_percent TINYINT UNSIGNED NOT NULL DEFAULT 30;

-- 2. Telemetria Unificada do Discovery, Crawler e IA (FASE 3)
CREATE TABLE IF NOT EXISTS discovery_run_telemetry (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  service_name VARCHAR(60) NOT NULL,
  started_at DATETIME(6) NOT NULL,
  finished_at DATETIME(6) NOT NULL,
  duration_ms INT NOT NULL,
  source_queried VARCHAR(100) NOT NULL,
  records_created INT NOT NULL DEFAULT 0,
  records_updated INT NOT NULL DEFAULT 0,
  api_units_consumed INT NOT NULL DEFAULT 0,
  status ENUM('success', 'warning', 'error') NOT NULL DEFAULT 'success',
  error_message VARCHAR(500) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_telemetry_time (started_at),
  KEY ix_telemetry_service (service_name, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Inserção Idempotente de Todos os 144 Municípios Oficiais do Estado do Pará (IBGE)
INSERT INTO geo_municipalities (ibge_code, name, normalized_name, mesoregion, microregion, is_tourist_pole, priority_level)
VALUES
  (1500107, 'Abaetetuba', 'abaetetuba', 'Nordeste Paraense', 'Cametá', FALSE, 2),
  (1500131, 'Abel Figueiredo', 'abel figueiredo', 'Sudeste Paraense', 'Paragominas', FALSE, 2),
  (1500206, 'Acará', 'acara', 'Nordeste Paraense', 'Tomé-Açu', FALSE, 2),
  (1500305, 'Afuá', 'afua', 'Marajó', 'Furos de Breves', FALSE, 2),
  (1500347, 'Água Azul do Norte', 'agua azul do norte', 'Sudeste Paraense', 'Parauapebas', FALSE, 2),
  (1500404, 'Alenquer', 'alenquer', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1500503, 'Almeirim', 'almeirim', 'Baixo Amazonas', 'Almeirim', FALSE, 2),
  (1500602, 'Altamira', 'altamira', 'Sudoeste Paraense', 'Altamira', TRUE, 1),
  (1500701, 'Anajás', 'anajas', 'Marajó', 'Furos de Breves', FALSE, 2),
  (1500800, 'Ananindeua', 'ananindeua', 'Metropolitana de Belém', 'Belém', TRUE, 1),
  (1500859, 'Anapu', 'anapu', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1500909, 'Augusto Corrêa', 'augusto correa', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1500958, 'Aurora do Pará', 'aurora do para', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1501006, 'Aveiro', 'aveiro', 'Sudoeste Paraense', 'Itaituba', FALSE, 2),
  (1501105, 'Bagre', 'bagre', 'Marajó', 'Portel', FALSE, 2),
  (1501204, 'Baião', 'baiao', 'Nordeste Paraense', 'Cametá', FALSE, 2),
  (1501253, 'Bannach', 'bannach', 'Sudeste Paraense', 'São Félix do Xingu', FALSE, 2),
  (1501303, 'Barcarena', 'barcarena', 'Metropolitana de Belém', 'Belém', TRUE, 1),
  (1501402, 'Belém', 'belem', 'Metropolitana de Belém', 'Belém', TRUE, 1),
  (1501451, 'Belterra', 'belterra', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1501501, 'Benevides', 'benevides', 'Metropolitana de Belém', 'Belém', FALSE, 1),
  (1501576, 'Bom Jesus do Tocantins', 'bom jesus do tocantins', 'Sudeste Paraense', 'Paragominas', FALSE, 2),
  (1501600, 'Bonito', 'bonito', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1501709, 'Bragança', 'braganca', 'Nordeste Paraense', 'Bragantina', TRUE, 1),
  (1501725, 'Brasil Novo', 'brasil novo', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1501758, 'Brejo Grande do Araguaia', 'brejo grande do araguaia', 'Sudeste Paraense', 'Marabá', FALSE, 2),
  (1501782, 'Breu Branco', 'breu branco', 'Sudeste Paraense', 'Tucuruí', FALSE, 2),
  (1501808, 'Breves', 'breves', 'Marajó', 'Furos de Breves', FALSE, 2),
  (1501907, 'Bujaru', 'bujaru', 'Metropolitana de Belém', 'Castanhal', FALSE, 1),
  (1501956, 'Cachoeira do Piriá', 'cachoeira do piria', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1502004, 'Cachoeira do Arari', 'cachoeira do arari', 'Marajó', 'Arari', FALSE, 2),
  (1502103, 'Cametá', 'cameta', 'Nordeste Paraense', 'Cametá', TRUE, 1),
  (1502152, 'Canaã dos Carajás', 'canaa dos carajas', 'Sudeste Paraense', 'Parauapebas', FALSE, 2),
  (1502202, 'Capanema', 'capanema', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1502301, 'Capitão Poço', 'capitao poco', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1502400, 'Castanhal', 'castanhal', 'Metropolitana de Belém', 'Castanhal', TRUE, 1),
  (1502509, 'Chaves', 'chaves', 'Marajó', 'Arari', FALSE, 2),
  (1502608, 'Colares', 'colares', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1502707, 'Conceição do Araguaia', 'conceicao do araguaia', 'Sudeste Paraense', 'Conceição do Araguaia', TRUE, 1),
  (1502756, 'Concórdia do Pará', 'concordia do para', 'Nordeste Paraense', 'Tomé-Açu', FALSE, 2),
  (1502764, 'Cumaru do Norte', 'cumaru do norte', 'Sudeste Paraense', 'São Félix do Xingu', FALSE, 2),
  (1502772, 'Curionópolis', 'curionopolis', 'Sudeste Paraense', 'Parauapebas', FALSE, 2),
  (1502806, 'Curralinho', 'curralinho', 'Marajó', 'Furos de Breves', FALSE, 2),
  (1502855, 'Curuá', 'curua', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1502905, 'Curuçá', 'curuca', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1502939, 'Dom Eliseu', 'dom eliseu', 'Sudeste Paraense', 'Paragominas', FALSE, 2),
  (1502954, 'Eldorado do Carajás', 'eldorado do carajas', 'Sudeste Paraense', 'Parauapebas', FALSE, 2),
  (1503002, 'Faro', 'faro', 'Baixo Amazonas', 'Óbidos', FALSE, 2),
  (1503044, 'Floresta do Araguaia', 'floresta do araguaia', 'Sudeste Paraense', 'Conceição do Araguaia', FALSE, 2),
  (1503077, 'Garrafão do Norte', 'garrafao do norte', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1503093, 'Goianésia do Pará', 'goianesia do para', 'Sudeste Paraense', 'Paragominas', FALSE, 2),
  (1503101, 'Gurupá', 'gurupa', 'Marajó', 'Portel', FALSE, 2),
  (1503200, 'Igarapé-Açu', 'igarape acu', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1503309, 'Igarapé-Miri', 'igarape miri', 'Nordeste Paraense', 'Cametá', FALSE, 2),
  (1503408, 'Inhangapi', 'inhangapi', 'Metropolitana de Belém', 'Castanhal', FALSE, 1),
  (1503457, 'Ipixuna do Pará', 'ipixuna do para', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1503507, 'Irituia', 'irituia', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1503606, 'Itaituba', 'itaituba', 'Sudoeste Paraense', 'Itaituba', FALSE, 2),
  (1503705, 'Itupiranga', 'itupiranga', 'Sudeste Paraense', 'Tucuruí', FALSE, 2),
  (1503754, 'Jacareacanga', 'jacareacanga', 'Sudoeste Paraense', 'Itaituba', FALSE, 2),
  (1503804, 'Jacundá', 'jacunda', 'Sudeste Paraense', 'Tucuruí', FALSE, 2),
  (1503903, 'Juruti', 'juruti', 'Baixo Amazonas', 'Óbidos', FALSE, 2),
  (1504000, 'Limoeiro do Ajuru', 'limoeiro do ajuru', 'Nordeste Paraense', 'Cametá', FALSE, 2),
  (1504059, 'Mãe do Rio', 'mae do rio', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1504109, 'Magalhães Barata', 'magalhaes barata', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1504208, 'Marabá', 'maraba', 'Sudeste Paraense', 'Marabá', TRUE, 1),
  (1504307, 'Maracanã', 'maracana', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1504406, 'Marapanim', 'marapanim', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1504422, 'Marituba', 'marituba', 'Metropolitana de Belém', 'Belém', FALSE, 1),
  (1504455, 'Medicilândia', 'medicilandia', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1504505, 'Melgaço', 'melgaco', 'Marajó', 'Portel', FALSE, 2),
  (1504604, 'Mocajuba', 'mocajuba', 'Nordeste Paraense', 'Cametá', FALSE, 2),
  (1504703, 'Moju', 'moju', 'Nordeste Paraense', 'Tomé-Açu', FALSE, 2),
  (1504752, 'Mojuí dos Campos', 'mojui dos campos', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1504802, 'Monte Alegre', 'monte alegre', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1504901, 'Muaná', 'muana', 'Marajó', 'Arari', FALSE, 2),
  (1504950, 'Nova Esperança do Piriá', 'nova esperanca do piria', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1504976, 'Nova Ipixuna', 'nova ipixuna', 'Sudeste Paraense', 'Tucuruí', FALSE, 2),
  (1505007, 'Nova Timboteua', 'nova timboteua', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1505031, 'Novo Progresso', 'novo progresso', 'Sudoeste Paraense', 'Itaituba', FALSE, 2),
  (1505064, 'Novo Repartimento', 'novo repartimento', 'Sudeste Paraense', 'Tucuruí', FALSE, 2),
  (1505106, 'Óbidos', 'obidos', 'Baixo Amazonas', 'Óbidos', TRUE, 1),
  (1505205, 'Oeiras do Pará', 'oeiras do para', 'Nordeste Paraense', 'Cametá', FALSE, 2),
  (1505304, 'Oriximiná', 'oriximina', 'Baixo Amazonas', 'Óbidos', FALSE, 2),
  (1505403, 'Ourém', 'ourem', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1505437, 'Ourilândia do Norte', 'ourilandia do norte', 'Sudeste Paraense', 'São Félix do Xingu', FALSE, 2),
  (1505486, 'Pacajá', 'pacaja', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1505494, 'Palestina do Pará', 'palestina do para', 'Sudeste Paraense', 'Marabá', FALSE, 2),
  (1505502, 'Paragominas', 'paragominas', 'Sudeste Paraense', 'Paragominas', TRUE, 1),
  (1505536, 'Parauapebas', 'parauapebas', 'Sudeste Paraense', 'Parauapebas', TRUE, 1),
  (1505551, 'Pau D''Arco', 'pau darco', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1505601, 'Peixe-Boi', 'peixe boi', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1505635, 'Piçarra', 'picarra', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1505650, 'Placas', 'placas', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1505700, 'Ponta de Pedras', 'ponta de pedras', 'Marajó', 'Arari', FALSE, 2),
  (1505809, 'Portel', 'portel', 'Marajó', 'Portel', FALSE, 2),
  (1505908, 'Porto de Moz', 'porto de moz', 'Baixo Amazonas', 'Almeirim', FALSE, 2),
  (1506005, 'Prainha', 'prainha', 'Baixo Amazonas', 'Santarém', FALSE, 2),
  (1506104, 'Primavera', 'primavera', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1506112, 'Quatipuru', 'quatipuru', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1506138, 'Redenção', 'redencao', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1506161, 'Rio Maria', 'rio maria', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1506187, 'Rondon do Pará', 'rondon do para', 'Sudeste Paraense', 'Paragominas', FALSE, 2),
  (1506195, 'Rurópolis', 'ruropolis', 'Sudoeste Paraense', 'Itaituba', FALSE, 2),
  (1506203, 'Salinópolis', 'salinopolis', 'Nordeste Paraense', 'Salgado', TRUE, 1),
  (1506302, 'Salvaterra', 'salvaterra', 'Marajó', 'Arari', TRUE, 1),
  (1506351, 'Santa Bárbara do Pará', 'santa barbara do para', 'Metropolitana de Belém', 'Belém', FALSE, 1),
  (1506401, 'Santa Cruz do Arari', 'santa cruz do arari', 'Marajó', 'Arari', FALSE, 2),
  (1506500, 'Santa Izabel do Pará', 'santa izabel do para', 'Metropolitana de Belém', 'Castanhal', FALSE, 1),
  (1506559, 'Santa Luzia do Pará', 'santa luzia do para', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1506583, 'Santa Maria das Barreiras', 'santa maria das barreiras', 'Sudeste Paraense', 'Conceição do Araguaia', FALSE, 2),
  (1506609, 'Santa Maria do Pará', 'santa maria do para', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1506708, 'Santana do Araguaia', 'santana do araguaia', 'Sudeste Paraense', 'Conceição do Araguaia', FALSE, 2),
  (1506807, 'Santarém', 'santarem', 'Baixo Amazonas', 'Santarém', TRUE, 1),
  (1506906, 'Santarém Novo', 'santarem novo', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1507003, 'Santo Antônio do Tauá', 'santo antonio do taua', 'Metropolitana de Belém', 'Castanhal', FALSE, 1),
  (1507102, 'São Caetano de Odivelas', 'sao caetano de odivelas', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1507151, 'São Domingos do Araguaia', 'sao domingos do araguaia', 'Sudeste Paraense', 'Marabá', FALSE, 2),
  (1507201, 'São Domingos do Capim', 'sao domingos do capim', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1507300, 'São Félix do Xingu', 'sao felix do xingu', 'Sudeste Paraense', 'São Félix do Xingu', FALSE, 2),
  (1507409, 'São Francisco do Pará', 'sao francisco do para', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1507458, 'São Geraldo do Araguaia', 'sao geraldo do araguaia', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1507466, 'São João da Ponta', 'sao joao da ponta', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1507474, 'São João de Pirabas', 'sao joao de pirabas', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1507508, 'São João do Araguaia', 'sao joao do araguaia', 'Sudeste Paraense', 'Marabá', FALSE, 2),
  (1507607, 'São Miguel do Guamá', 'sao miguel do guama', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1507706, 'São Sebastião da Boa Vista', 'sao sebastiao da boa vista', 'Marajó', 'Furos de Breves', FALSE, 2),
  (1507755, 'Sapucaia', 'sapucaia', 'Sudeste Paraense', 'Redenção', FALSE, 2),
  (1507805, 'Senador José Porfírio', 'senador jose porfirio', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1507904, 'Soure', 'soure', 'Marajó', 'Arari', TRUE, 1),
  (1507953, 'Tailândia', 'tailandia', 'Nordeste Paraense', 'Tomé-Açu', FALSE, 2),
  (1507961, 'Terra Alta', 'terra alta', 'Nordeste Paraense', 'Salgado', FALSE, 2),
  (1507979, 'Terra Santa', 'terra santa', 'Baixo Amazonas', 'Óbidos', FALSE, 2),
  (1508001, 'Tomé-Açu', 'tome acu', 'Nordeste Paraense', 'Tomé-Açu', FALSE, 2),
  (1508035, 'Tracuateua', 'tracuateua', 'Nordeste Paraense', 'Bragantina', FALSE, 2),
  (1508050, 'Trairão', 'trairao', 'Sudoeste Paraense', 'Itaituba', FALSE, 2),
  (1508084, 'Tucumã', 'tucuma', 'Sudeste Paraense', 'São Félix do Xingu', FALSE, 2),
  (1508100, 'Tucuruí', 'tucurui', 'Sudeste Paraense', 'Tucuruí', FALSE, 2),
  (1508126, 'Ulianópolis', 'ulianopolis', 'Sudeste Paraense', 'Paragominas', FALSE, 2),
  (1508159, 'Uruará', 'uruara', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1508209, 'Vigia', 'vigia', 'Nordeste Paraense', 'Salgado', TRUE, 1),
  (1508308, 'Viseu', 'viseu', 'Nordeste Paraense', 'Guamá', FALSE, 2),
  (1508357, 'Vitória do Xingu', 'vitoria do xingu', 'Sudoeste Paraense', 'Altamira', FALSE, 2),
  (1508407, 'Xinguara', 'xinguara', 'Sudeste Paraense', 'Redenção', FALSE, 2)
ON DUPLICATE KEY UPDATE
  name = VALUES(name),
  normalized_name = VALUES(normalized_name),
  mesoregion = VALUES(mesoregion),
  microregion = VALUES(microregion),
  is_tourist_pole = VALUES(is_tourist_pole),
  priority_level = VALUES(priority_level);

-- 4. Complemento de Distritos, Ilhas e Polos Gastronômicos Oficiais
INSERT IGNORE INTO geo_districts_localities (municipality_ibge, name, normalized_name, locality_type, is_tourist_highlight)
VALUES
  (1504307, 'Vila de Algodoal / Ilha de Maiandeua', 'algodoal', 'island', TRUE),
  (1504406, 'Praia de Marudá', 'maruda', 'tourist_zone', TRUE),
  (1506304, 'Vila Histórica de Joanes', 'joanes', 'district', TRUE),
  (1506304, 'Praia Grande de Salvaterra', 'praia grande salvaterra', 'tourist_zone', TRUE),
  (1501402, 'Praia do Farol / Ilha de Mosqueiro', 'praia do farol mosqueiro', 'tourist_zone', TRUE),
  (1501402, 'Praia do Murubira / Ilha de Mosqueiro', 'praia do murubira mosqueiro', 'tourist_zone', TRUE),
  (1501402, 'Vila / Ilha de Mosqueiro', 'vila mosqueiro', 'district', TRUE),
  (1501402, 'Praia Grande de Outeiro', 'praia grande outeiro', 'tourist_zone', TRUE),
  (1501402, 'Praia do Amor / Ilha de Outeiro', 'praia do amor outeiro', 'tourist_zone', TRUE),
  (1501402, 'Praia da Saudade / Ilha de Cotijuba', 'praia da saudade cotijuba', 'tourist_zone', TRUE);

INSERT INTO schema_migrations(id,name,checksum,applied_at) VALUES('015','cobertura estadual integral 144 municipios do para, telemetria e completude de catalogo','362E83C3E7833150506F4D39CA3D973F7B175673D58F6707564122A7B56E09F5',UTC_TIMESTAMP(6));
INSERT INTO schema_migration_attempts(id,checksum,started_at,completed_at) VALUES('015','362E83C3E7833150506F4D39CA3D973F7B175673D58F6707564122A7B56E09F5',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));

-- SANIDADE FINAL: deve retornar 15 migrations, 0 tentativas incompletas, e as 3 tabelas abaixo.
SELECT COUNT(*) AS migrations_aplicadas FROM schema_migrations;
SELECT COUNT(*) AS migracoes_interrompidas FROM schema_migration_attempts WHERE completed_at IS NULL;
SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'belrestaurante' AND TABLE_NAME IN ('user_experiences','restaurant_review_replies','gastronomic_trails');
