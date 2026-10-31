-- Character organization and persistent wanted status for police commands.
BEGIN;
SET LOCAL ROLE hbrp_owner;
INSERT INTO hbrp.organizations(code,name,kind)
VALUES('pm','Polícia Militar','public') ON CONFLICT(code) DO NOTHING;
CREATE UNIQUE INDEX organization_members_one_character
 ON hbrp.organization_members(character_id);
GRANT INSERT,UPDATE ON hbrp.organization_members TO hbrp_app;
CREATE TABLE hbrp.character_wanted (
 character_id uuid PRIMARY KEY REFERENCES hbrp.characters(id),
 stars smallint NOT NULL CHECK(stars BETWEEN 1 AND 6),
 reason varchar(180) NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 180),
 issued_by uuid NOT NULL REFERENCES hbrp.accounts(id),
 issued_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now()
);
GRANT SELECT,INSERT,UPDATE,DELETE ON hbrp.character_wanted TO hbrp_app;
INSERT INTO hbrp.schema_migrations(version) VALUES(9);
COMMIT;
