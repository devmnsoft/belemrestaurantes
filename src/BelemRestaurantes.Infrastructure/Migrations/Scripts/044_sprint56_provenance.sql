-- Sprint 56: proveniência, pratos, vídeos e ingredientes. Sem dados pessoais de sócios.

ALTER TABLE restaurants
  ADD COLUMN phone_secondary VARCHAR(40) NULL,
  ADD COLUMN coordinate_precision VARCHAR(24) NULL,
  ADD COLUMN coordinate_source VARCHAR(180) NULL;

CREATE TABLE IF NOT EXISTS field_provenance (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  field_name VARCHAR(40) NOT NULL,
  field_value VARCHAR(500) NULL,
  state VARCHAR(24) NOT NULL,
  source_url VARCHAR(800) NULL,
  source_name VARCHAR(180) NULL,
  collected_at DATETIME(6) NOT NULL,
  published_at DATETIME(6) NULL,
  evidence VARCHAR(500) NULL,
  method VARCHAR(40) NOT NULL,
  confidence VARCHAR(24) NOT NULL,
  source_key CHAR(64) NOT NULL,
  UNIQUE KEY uq_field_source (restaurant_id, field_name, source_key),
  KEY ix_field_restaurant (restaurant_id),
  CONSTRAINT fk_field_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS place_dishes (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NULL,
  name VARCHAR(180) NOT NULL,
  attribution VARCHAR(40) NOT NULL,
  note VARCHAR(500) NULL,
  source_url VARCHAR(800) NULL,
  availability VARCHAR(40) NOT NULL DEFAULT 'unknown',
  observed_at DATETIME(6) NOT NULL,
  KEY ix_place_dishes_restaurant (restaurant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS culinary_videos (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  title VARCHAR(220) NOT NULL,
  author_name VARCHAR(180) NOT NULL,
  platform VARCHAR(40) NOT NULL,
  canonical_url VARCHAR(800) NOT NULL,
  published_on DATE NULL,
  related_topic VARCHAR(120) NULL,
  summary VARCHAR(500) NOT NULL,
  attribution_kind VARCHAR(40) NOT NULL,
  url_hash CHAR(64) NOT NULL,
  observed_at DATETIME(6) NOT NULL,
  UNIQUE KEY uq_culinary_video (url_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS normalized_ingredients (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(120) NOT NULL,
  synonyms VARCHAR(300) NULL,
  unit VARCHAR(20) NOT NULL,
  regional_name VARCHAR(120) NULL,
  UNIQUE KEY uq_ingredient_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ingredient_offers (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  ingredient_id BIGINT UNSIGNED NOT NULL,
  product_name VARCHAR(180) NOT NULL,
  seller VARCHAR(180) NOT NULL,
  quantity DECIMAL(12,3) NOT NULL,
  unit VARCHAR(20) NOT NULL,
  price DECIMAL(12,2) NOT NULL,
  freight DECIMAL(12,2) NULL,
  available_in VARCHAR(120) NULL,
  source_url VARCHAR(800) NOT NULL,
  observed_at DATETIME(6) NOT NULL,
  KEY ix_offer_ingredient (ingredient_id, observed_at),
  CONSTRAINT fk_offer_ingredient FOREIGN KEY (ingredient_id) REFERENCES normalized_ingredients (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

UPDATE restaurants
SET phone_secondary = '9193045153'
WHERE created_source_url = 'https://minhareceita.org/44117434000141'
  AND (phone_secondary IS NULL OR phone_secondary = '');

UPDATE restaurants
SET coordinate_precision = 'approximate',
    coordinate_source = 'OpenStreetMap node 12708166615'
WHERE created_source_url = 'https://www.openstreetmap.org/node/12708166615'
  AND latitude IS NOT NULL
  AND (coordinate_precision IS NULL OR coordinate_precision = '');

INSERT INTO field_provenance (restaurant_id, field_name, field_value, state, source_url, source_name, collected_at, published_at, evidence, method, confidence, source_key)
SELECT r.id, 'address', 'Travessa Três de Maio, 1680, São Brás, Belém, CEP 66063-388', 'informed',
  'https://minhareceita.org/44117434000141', 'Cadastro público de CNPJ', UTC_TIMESTAMP(6), '2021-11-04',
  'Logradouro, número, bairro e CEP do cadastro da empresa. Situação cadastral ativa na consulta de 2026-10-21. Sem dados de sócios.',
  'registry_api', 'informed', SHA2('address|cnpj-44117434000141', 256)
FROM restaurants r
WHERE r.created_source_url = 'https://minhareceita.org/44117434000141'
  AND NOT EXISTS (SELECT 1 FROM field_provenance p WHERE p.restaurant_id = r.id AND p.source_key = SHA2('address|cnpj-44117434000141', 256));

INSERT INTO field_provenance (restaurant_id, field_name, field_value, state, source_url, source_name, collected_at, evidence, method, confidence, source_key)
SELECT r.id, 'phone', '9193172901 e 9193045153', 'informed',
  'https://minhareceita.org/44117434000141', 'Cadastro público de CNPJ', UTC_TIMESTAMP(6),
  'Dois telefones comerciais publicados no mesmo cadastro. O importador antigo gravou só o primeiro.',
  'registry_api', 'informed', SHA2('phone|cnpj-44117434000141', 256)
FROM restaurants r
WHERE r.created_source_url = 'https://minhareceita.org/44117434000141'
  AND NOT EXISTS (SELECT 1 FROM field_provenance p WHERE p.restaurant_id = r.id AND p.source_key = SHA2('phone|cnpj-44117434000141', 256));

INSERT INTO field_provenance (restaurant_id, field_name, field_value, state, source_url, source_name, collected_at, published_at, evidence, method, confidence, source_key)
SELECT r.id, 'address', 'OSM: Avenida Gentil Bittencourt, 1680, São Bráz, CEP 66063-388. Coordenada do nó não copiada para esta ficha.', 'conflict',
  'https://www.openstreetmap.org/node/12708166615', 'OpenStreetMap', UTC_TIMESTAMP(6), '2026-06-22',
  'O nó OSM usa outra rua para o número 1680 e o mesmo CEP. A coordenada permanece só na ficha de origem OSM e não é tratada como ponto exato desta unidade.',
  'osm_node', 'conflict', SHA2('address|osm-12708166615', 256)
FROM restaurants r
WHERE r.created_source_url = 'https://minhareceita.org/44117434000141'
  AND NOT EXISTS (SELECT 1 FROM field_provenance p WHERE p.restaurant_id = r.id AND p.source_key = SHA2('address|osm-12708166615', 256));

INSERT INTO field_provenance (restaurant_id, field_name, field_value, state, source_url, source_name, collected_at, evidence, method, confidence, source_key)
SELECT r.id, 'hours', NULL, 'not_located',
  'https://minhareceita.org/44117434000141', 'Cadastro público de CNPJ', UTC_TIMESTAMP(6),
  'O cadastro não publica horário. Agregadores consultados divergem entre si e não foram gravados como horário vigente.',
  'registry_api', 'not_located', SHA2('hours|cnpj-44117434000141', 256)
FROM restaurants r
WHERE r.created_source_url = 'https://minhareceita.org/44117434000141'
  AND NOT EXISTS (SELECT 1 FROM field_provenance p WHERE p.restaurant_id = r.id AND p.source_key = SHA2('hours|cnpj-44117434000141', 256));

INSERT INTO culinary_videos (title, author_name, platform, canonical_url, published_on, related_topic, summary, attribution_kind, url_hash, observed_at)
SELECT 'O tacacá original do Pará.', 'Culinária Paraense', 'YouTube', 'https://www.youtube.com/watch?v=mwusmxyNpbU', '2021-08-21', 'tacacá',
  'O canal apresenta um preparo de tacacá. Não é receita oficial de um restaurante e a página não trata o vídeo como cardápio.',
  'channel_demonstration', SHA2('https://www.youtube.com/watch?v=mwusmxyNpbU', 256), UTC_TIMESTAMP(6)
WHERE NOT EXISTS (SELECT 1 FROM culinary_videos WHERE url_hash = SHA2('https://www.youtube.com/watch?v=mwusmxyNpbU', 256));

INSERT INTO culinary_videos (title, author_name, platform, canonical_url, published_on, related_topic, summary, attribution_kind, url_hash, observed_at)
SELECT 'Égua Doido - Como fazer Tacacá (receita típica do Pará)', 'Cansei de Ser Chef', 'YouTube', 'https://www.youtube.com/watch?v=srYZOADHZOg', '2020-02-13', 'tacacá',
  'Aula em vídeo sobre tacacá paraense. Versão de canal, não atribuída a um estabelecimento de Belém.',
  'traditional_similar', SHA2('https://www.youtube.com/watch?v=srYZOADHZOg', 256), UTC_TIMESTAMP(6)
WHERE NOT EXISTS (SELECT 1 FROM culinary_videos WHERE url_hash = SHA2('https://www.youtube.com/watch?v=srYZOADHZOg', 256));

INSERT INTO culinary_videos (title, author_name, platform, canonical_url, published_on, related_topic, summary, attribution_kind, url_hash, observed_at)
SELECT 'Vem Cozinhar pro Círio - Arroz Paraense', 'Banco da Amazônia', 'YouTube', 'https://www.youtube.com/watch?v=vvXIIWGqLhA', '2026-10-10', 'arroz paraense',
  'Série do Banco da Amazônia com o chef Everaldo Akihyto sobre arroz paraense. Não é o cardápio de uma pizzaria.',
  'chef_published', SHA2('https://www.youtube.com/watch?v=vvXIIWGqLhA', 256), UTC_TIMESTAMP(6)
WHERE NOT EXISTS (SELECT 1 FROM culinary_videos WHERE url_hash = SHA2('https://www.youtube.com/watch?v=vvXIIWGqLhA', 256));

INSERT INTO normalized_ingredients (name, synonyms, unit, regional_name)
SELECT 'tucupi', 'caldo de mandioca brava', 'litro', 'tucupi'
WHERE NOT EXISTS (SELECT 1 FROM normalized_ingredients WHERE name = 'tucupi');

INSERT INTO normalized_ingredients (name, synonyms, unit, regional_name)
SELECT 'jambu', 'agrião-do-pará', 'maço', 'jambu'
WHERE NOT EXISTS (SELECT 1 FROM normalized_ingredients WHERE name = 'jambu');

INSERT INTO normalized_ingredients (name, synonyms, unit, regional_name)
SELECT 'goma de tapioca', 'goma de mandioca', 'grama', 'goma'
WHERE NOT EXISTS (SELECT 1 FROM normalized_ingredients WHERE name = 'goma de tapioca');
