BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
ALTER TABLE beta_access ADD COLUMN login_name text;
ALTER TABLE beta_access ADD CONSTRAINT beta_access_login_name_format CHECK(login_name IS NULL OR login_name ~ '^user[0-9]{2}$');
CREATE UNIQUE INDEX beta_access_login_name_unique ON beta_access(lower(login_name)) WHERE login_name IS NOT NULL;
COMMENT ON COLUMN beta_access.login_name IS 'Optional team-issued login alias; account UID remains permanent.';
INSERT INTO schema_migrations(version) VALUES(13);
COMMIT;
