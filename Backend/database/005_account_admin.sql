-- Additive account permissions. Promotion is an administrative database operation.
BEGIN;
ALTER TABLE hbrp.accounts ADD COLUMN account_role text NOT NULL DEFAULT 'user', ADD COLUMN admin_level integer NOT NULL DEFAULT 0;
ALTER TABLE hbrp.accounts ADD CONSTRAINT accounts_admin_role CHECK ((account_role='user' AND admin_level=0) OR (account_role='admin' AND admin_level>0));
COMMENT ON COLUMN hbrp.accounts.admin_level IS '0: jogador normal; positivo: administrador. Não editável pela API.';
INSERT INTO hbrp.schema_migrations(version) VALUES(5);
COMMIT;
