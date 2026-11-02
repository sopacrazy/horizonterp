-- Additive alias expansion; numeric UID login and closed-beta grants stay intact.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
ALTER TABLE beta_access DROP CONSTRAINT beta_access_login_name_format;
ALTER TABLE beta_access ADD CONSTRAINT beta_access_login_name_format
 CHECK(login_name IS NULL OR login_name ~ '^[A-Za-z][A-Za-z0-9_]{2,31}$');
COMMENT ON COLUMN beta_access.login_name IS 'Optional operator-issued case-insensitive login alias: 3-32 ASCII letters/digits/underscore, starting with a letter. Never grants an administrative role; account UID remains permanent.';
INSERT INTO schema_migrations(version) VALUES(14);
COMMIT;
