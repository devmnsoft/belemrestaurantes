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
