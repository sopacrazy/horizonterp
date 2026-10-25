-- Additive moderation state; all character data and saves remain in place.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
ALTER TABLE characters
 ADD COLUMN blocked_at timestamptz,
 ADD COLUMN blocked_by uuid REFERENCES accounts(id),
 ADD COLUMN blocked_reason varchar(180);
GRANT UPDATE(account_role,admin_level) ON accounts TO hbrp_app;
COMMENT ON COLUMN accounts.admin_level IS '0: jogador; positivo: administrador. Alterações pela API exigem administrador e auditoria.';
INSERT INTO schema_migrations(version) VALUES(6);
COMMIT;
