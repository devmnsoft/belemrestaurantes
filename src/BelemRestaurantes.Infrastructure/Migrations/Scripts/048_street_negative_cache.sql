-- Sprint 65: cache negativo de "logradouro vazio no OSM" para a geocodificação.
-- Guarda, por escopo (logradouro normalizado, cidade normalizada), quando uma busca real de nível
-- de logradouro retornou zero candidatos no Nominatim. Dentro da janela de validade, fichas do
-- mesmo escopo são encerradas como no_candidates_street sem consumir cota diária.
CREATE TABLE IF NOT EXISTS geocoding_street_cache (
  street_key VARCHAR(160) NOT NULL,
  city_key VARCHAR(100) NOT NULL,
  learned_at DATETIME(6) NOT NULL,
  PRIMARY KEY (street_key, city_key),
  KEY ix_street_cache_learned (learned_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
