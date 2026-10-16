-- name: contas da comunidade, sessões, tokens únicos e outbox privada
CREATE TABLE member_users (
 id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 full_name VARCHAR(160) NOT NULL,
 email VARCHAR(200) NOT NULL,
 normalized_email VARCHAR(200) NOT NULL,
 password_hash VARCHAR(255) NOT NULL,
 security_stamp CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 email_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
 is_blocked BOOLEAN NOT NULL DEFAULT FALSE,
 failed_attempts INT UNSIGNED NOT NULL DEFAULT 0,
 lockout_until DATETIME(6) NULL,
 preferred_culture VARCHAR(16) NOT NULL DEFAULT 'pt-BR',
 terms_version VARCHAR(40) NOT NULL,
 terms_accepted_at DATETIME(6) NOT NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_member_email(normalized_email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE member_sessions (
 id_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 security_stamp CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 revoked_at DATETIME(6) NULL,
 KEY ix_member_sessions(user_id, revoked_at, expires_at),
 CONSTRAINT fk_member_session FOREIGN KEY(user_id) REFERENCES member_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE member_tokens (
 token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 purpose ENUM('confirm','reset') NOT NULL,
 security_stamp CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 used_at DATETIME(6) NULL,
 KEY ix_member_tokens(user_id, purpose, created_at),
 CONSTRAINT fk_member_token FOREIGN KEY(user_id) REFERENCES member_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE transactional_email_outbox (
 id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 payload MEDIUMBLOB NULL,
 state ENUM('pending','sending','sent','dead_lettered') NOT NULL DEFAULT 'pending',
 attempts INT UNSIGNED NOT NULL DEFAULT 0,
 next_attempt_at DATETIME(6) NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 lease_id CHAR(32) NULL,
 locked_until DATETIME(6) NULL,
 last_error VARCHAR(80) NULL,
 completed_at DATETIME(6) NULL,
 KEY ix_email_claim(state,next_attempt_at,locked_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
