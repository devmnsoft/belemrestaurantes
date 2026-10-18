CREATE TABLE para_recipes (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,slug VARCHAR(120) NOT NULL UNIQUE,
 title VARCHAR(160) NOT NULL,category VARCHAR(80) NOT NULL,course VARCHAR(40) NOT NULL,city VARCHAR(100) NOT NULL,
 total_minutes INT NOT NULL,status VARCHAR(20) NOT NULL DEFAULT 'draft',version INT NOT NULL DEFAULT 1,
 body JSON NOT NULL,search_text TEXT NOT NULL,updated_at DATETIME(6) NOT NULL,updated_by VARCHAR(160) NOT NULL,
 KEY ix_recipe_public(status,category,updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE para_recipe_versions (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,recipe_id BIGINT UNSIGNED NOT NULL,
 version INT NOT NULL,body JSON NOT NULL,actor VARCHAR(160) NOT NULL,created_at DATETIME(6) NOT NULL,
 UNIQUE KEY uq_recipe_version(recipe_id,version),FOREIGN KEY(recipe_id) REFERENCES para_recipes(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE para_recipe_restaurants (
 recipe_id BIGINT UNSIGNED NOT NULL,restaurant_id BIGINT UNSIGNED NOT NULL,PRIMARY KEY(recipe_id,restaurant_id),
 FOREIGN KEY(recipe_id) REFERENCES para_recipes(id) ON DELETE CASCADE,
 FOREIGN KEY(restaurant_id) REFERENCES restaurants(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
ALTER TABLE restaurants ADD COLUMN moderation_stage VARCHAR(24) NOT NULL DEFAULT 'pending';
UPDATE restaurants SET moderation_stage=CASE status WHEN 'published' THEN 'published' WHEN 'rejected' THEN 'rejected' WHEN 'suspended' THEN 'archived' ELSE 'pending' END;
CREATE TABLE operational_maintenance_jobs (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,kind VARCHAR(30) NOT NULL,state VARCHAR(20) NOT NULL DEFAULT 'pending',
 actor VARCHAR(160) NOT NULL,attempts INT NOT NULL DEFAULT 0,next_attempt_at DATETIME(6) NOT NULL,
 locked_until DATETIME(6) NULL,locked_by VARCHAR(80) NULL,last_error VARCHAR(200) NULL,result_json JSON NULL,
 created_at DATETIME(6) NOT NULL,completed_at DATETIME(6) NULL,
 KEY ix_maintenance_claim(state,next_attempt_at,locked_until),KEY ix_maintenance_kind(kind,state)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
