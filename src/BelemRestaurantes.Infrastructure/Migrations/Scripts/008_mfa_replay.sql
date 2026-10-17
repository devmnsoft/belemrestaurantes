-- name: proteção contra reutilização de códigos TOTP
ALTER TABLE admin_users ADD COLUMN last_totp_counter BIGINT NOT NULL DEFAULT -1;
