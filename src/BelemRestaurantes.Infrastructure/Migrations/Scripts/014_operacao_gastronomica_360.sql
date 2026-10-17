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
