-- name: governança administrativa da Sprint 02

ALTER TABLE admin_users ADD COLUMN is_disabled BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE admin_users ADD COLUMN password_changed_at DATETIME(6) NULL;

-- (mantém coluna 'disabled' em bases antigas, renomeia se necessário é feito pelo operador)
ALTER TABLE admin_users ADD COLUMN disabled BOOLEAN NOT NULL DEFAULT FALSE AFTER lockout_end_at;
ALTER TABLE admin_users ADD COLUMN updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) AFTER created_at;

-- Papéis da jornada editorial e comercial.
INSERT INTO admin_roles (id, name) VALUES
  (UUID(), 'content_editor'),
  (UUID(), 'advertiser'),
  (UUID(), 'restaurant_moderator'),
  (UUID(), 'platform_operator'),
  (UUID(), 'platform_admin')
ON DUPLICATE KEY UPDATE name = VALUES(name);

ALTER TABLE audit_events ADD INDEX ix_audit_actor (actor_subject, occurred_at);

CREATE INDEX ix_recovery_user_unused
  ON admin_recovery_codes (user_id, used_at);