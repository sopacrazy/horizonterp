-- Dedicated, non-login test character attached to one player's active character.
BEGIN;
SET LOCAL ROLE hbrp_owner;
CREATE TABLE hbrp.follow_test_bots (
 owner_character_id uuid PRIMARY KEY REFERENCES hbrp.characters(id),
 bot_character_id uuid NOT NULL UNIQUE REFERENCES hbrp.characters(id),
 enabled boolean NOT NULL DEFAULT true,
 created_at timestamptz NOT NULL DEFAULT now(),
 CHECK (owner_character_id <> bot_character_id)
);
GRANT SELECT ON hbrp.follow_test_bots TO hbrp_app;
INSERT INTO hbrp.schema_migrations(version) VALUES (8);
COMMIT;
