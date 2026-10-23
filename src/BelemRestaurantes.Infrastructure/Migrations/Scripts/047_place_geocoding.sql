-- Sprint 58: geocodificação de fichas sem coordenada (Nominatim/OpenStreetMap).
-- Fila persistente por ficha, com lease, tentativas e reserva diária compartilhada entre instâncias.

CREATE TABLE IF NOT EXISTS geocoding_tasks (
  restaurant_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
  state VARCHAR(24) NOT NULL DEFAULT 'pending',
  attempts INT NOT NULL DEFAULT 0,
  next_run_at DATETIME(6) NOT NULL,
  lease_until DATETIME(6) NULL,
  last_status VARCHAR(80) NULL,
  result_precision VARCHAR(24) NULL,
  osm_place_id BIGINT UNSIGNED NULL,
  updated_at DATETIME(6) NOT NULL,
  KEY ix_geocoding_queue (state,next_run_at),
  CONSTRAINT fk_geocoding_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS geocoding_runs (
  run_day DATE NOT NULL PRIMARY KEY,
  requested INT NOT NULL DEFAULT 0,
  granted INT NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
