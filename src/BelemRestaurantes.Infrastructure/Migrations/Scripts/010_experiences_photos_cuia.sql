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
