-- Expande as tabelas de horários já usadas pelo Portal Parceiro.
ALTER TABLE restaurant_schedules ADD COLUMN additional_intervals JSON NULL,
 ADD COLUMN is_24_hours BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE restaurant_special_schedules ADD COLUMN additional_intervals JSON NULL,
 ADD COLUMN is_24_hours BOOLEAN NOT NULL DEFAULT FALSE;
CREATE TABLE restaurant_operation_info (
 restaurant_id BIGINT UNSIGNED PRIMARY KEY, version INT NOT NULL DEFAULT 1,
 authority VARCHAR(30) NOT NULL, source_url VARCHAR(350) NOT NULL,
 confirmed_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NOT NULL,
 temporary_closed_until DATE NULL, reservation_only BOOLEAN NOT NULL DEFAULT FALSE,
 accessibility_note VARCHAR(500) NULL, updated_by VARCHAR(160) NOT NULL,
 CONSTRAINT fk_operation_restaurant FOREIGN KEY(restaurant_id) REFERENCES restaurants(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE restaurant_operation_history (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,restaurant_id BIGINT UNSIGNED NOT NULL,
 actor VARCHAR(160) NOT NULL,snapshot_json LONGTEXT NOT NULL,created_at DATETIME(6) NOT NULL,
 CONSTRAINT fk_operation_history FOREIGN KEY(restaurant_id) REFERENCES restaurants(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE mobility_sources (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,name VARCHAR(160) NOT NULL,kind VARCHAR(40) NOT NULL,
 source_url VARCHAR(350) NOT NULL,terms VARCHAR(500) NOT NULL,enabled BOOLEAN NOT NULL DEFAULT FALSE,
 checked_at DATETIME(6) NULL,expires_at DATETIME(6) NULL,last_sync_at DATETIME(6) NULL,
 last_error VARCHAR(500) NULL,refresh_minutes INT NOT NULL DEFAULT 10080,
 UNIQUE KEY uq_mobility_source(source_url)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE mobility_fares (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,source_id BIGINT UNSIGNED NOT NULL,
 service_name VARCHAR(160) NOT NULL,mode VARCHAR(30) NOT NULL,amount DECIMAL(9,2) NOT NULL,
 valid_from DATE NOT NULL,valid_until DATE NOT NULL,conditions VARCHAR(500) NOT NULL,
 confidence VARCHAR(30) NOT NULL,created_at DATETIME(6) NOT NULL,
 CONSTRAINT fk_fare_source FOREIGN KEY(source_id) REFERENCES mobility_sources(id),
 CHECK(amount>=0),CHECK(valid_until>=valid_from)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE mobility_api_usage (
 usage_day DATE NOT NULL,provider VARCHAR(40) NOT NULL,requests INT NOT NULL DEFAULT 0,
 failures INT NOT NULL DEFAULT 0,last_used_at DATETIME(6) NULL,last_error VARCHAR(100) NULL,
 PRIMARY KEY(usage_day,provider)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
