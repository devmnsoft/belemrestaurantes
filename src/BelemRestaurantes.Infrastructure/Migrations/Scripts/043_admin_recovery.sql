CREATE TABLE IF NOT EXISTS admin_password_tokens(
 token_hash CHAR(64) PRIMARY KEY,
 user_id VARCHAR(64) NOT NULL,
 credential_version CHAR(64) NOT NULL,
 created_at DATETIME(6) NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 used_at DATETIME(6) NULL,
 KEY ix_admin_reset_user(user_id,created_at)
);
