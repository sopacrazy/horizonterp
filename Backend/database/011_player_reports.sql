BEGIN;
SET LOCAL ROLE hbrp_owner;
CREATE TABLE hbrp.player_reports (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 character_id uuid NOT NULL REFERENCES hbrp.characters(id),
 message varchar(180) NOT NULL CHECK(length(btrim(message)) BETWEEN 3 AND 180),
 status text NOT NULL DEFAULT 'open' CHECK(status IN ('open','answered','closed')),
 answer varchar(500),
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX player_reports_character ON hbrp.player_reports(character_id,created_at DESC);
GRANT SELECT,INSERT,UPDATE ON hbrp.player_reports TO hbrp_app;
INSERT INTO hbrp.schema_migrations(version) VALUES(11);
COMMIT;
