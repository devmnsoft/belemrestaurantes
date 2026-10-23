-- Sprint 66: cache de respostas do Nominatim válido para o dia corrente (UTC).
-- A mesma consulta (endereço, cidade) respondida de verdade hoje é rejogada para buscas
-- idênticas sem consumir unidade do teto diário. Só respostas com fonte disponível e sem
-- restrição entram aqui; falha/restrrição nunca se congela. O mapa do OSM muda entre dias,
-- então a entrada expira com o dia UTC em que foi aprendida.
CREATE TABLE IF NOT EXISTS geocoding_query_cache (
  query_key CHAR(64) NOT NULL,
  day DATE NOT NULL,
  payload_json MEDIUMTEXT NOT NULL,
  hits INT NOT NULL DEFAULT 0,
  created_at DATETIME(6) NOT NULL,
  PRIMARY KEY (query_key, day),
  KEY ix_query_cache_day (day)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
