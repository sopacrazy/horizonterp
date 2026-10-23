-- Additive identity metadata. Existing names, characters, models and saves stay intact.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;

ALTER TABLE characters ALTER COLUMN name TYPE varchar(49);
ALTER TABLE characters
 ADD COLUMN first_name varchar(24),
 ADD COLUMN last_name varchar(24),
 ADD COLUMN gender text,
 ADD CONSTRAINT character_identity_names CHECK (
  (first_name IS NULL AND last_name IS NULL) OR
  (first_name IS NOT NULL AND last_name IS NOT NULL
   AND length(first_name) BETWEEN 2 AND 24 AND first_name=btrim(first_name)
   AND length(last_name) BETWEEN 2 AND 24 AND last_name=btrim(last_name)
   AND name=first_name||' '||last_name)
 ),
 ADD CONSTRAINT character_identity_gender CHECK (gender IS NULL OR gender IN ('female','male'));

-- Grants on characters are table-level and already cover the added columns.
-- The existing partial unique index still limits each account to one active character.
INSERT INTO schema_migrations(version) VALUES(4);
COMMIT;
