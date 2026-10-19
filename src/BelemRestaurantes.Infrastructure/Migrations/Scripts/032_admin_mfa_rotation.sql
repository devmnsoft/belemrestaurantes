-- name: rotação obrigatória do autenticador após código de recuperação
-- O MFA permanece ligado. A flag só obriga a confirmar um autenticador novo.
ALTER TABLE admin_users ADD COLUMN must_rotate_mfa BOOLEAN NOT NULL DEFAULT FALSE;
