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