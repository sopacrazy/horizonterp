-- Persisted activity sessions and server-authoritative interaction points.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;

ALTER TABLE criminal_activity_definitions ADD COLUMN duration_seconds integer NOT NULL DEFAULT 20
 CHECK(duration_seconds BETWEEN 5 AND 3600);
UPDATE criminal_activity_definitions SET duration_seconds=30 WHERE code='test_mining';

CREATE TABLE criminal_economy_locations(
 code text PRIMARY KEY CHECK(code ~ '^[a-z][a-z0-9_]{2,47}$'),
 kind text NOT NULL CHECK(kind IN ('activity','warehouse','craft')),
 activity_code text REFERENCES criminal_activity_definitions(code),
 organization_id uuid REFERENCES organizations(id),
 world text NOT NULL,
 label text NOT NULL CHECK(length(label) BETWEEN 3 AND 100),
 x double precision NOT NULL, y double precision NOT NULL, z double precision NOT NULL,
 radius double precision NOT NULL CHECK(radius>0 AND radius<=25),
 enabled boolean NOT NULL DEFAULT true,
 CHECK((kind='activity' AND activity_code IS NOT NULL AND organization_id IS NULL)
  OR (kind IN ('warehouse','craft') AND activity_code IS NULL AND organization_id IS NOT NULL))
);

-- Provisional points around the current Los Santos spawn; no memberships are seeded.
INSERT INTO criminal_economy_locations(code,kind,activity_code,world,label,x,y,z,radius) VALUES
 ('urban_recycling_ls','activity','urban_recycling','centro_teste','Coleta urbana',2481.3,12.37,1668.5,4),
 ('test_mining_ls','activity','test_mining','centro_teste','Mineração',2497.3,12.37,1668.5,4);
INSERT INTO criminal_economy_locations(code,kind,organization_id,world,label,x,y,z,radius)
SELECT p.code,p.kind,o.id,'centro_teste',p.label,p.x,12.37,p.z,4
FROM (VALUES
 ('faccao_roxa_warehouse','warehouse','faccao_roxa','Depósito da Facção Roxa',2489.3,1660.5),
 ('faccao_roxa_craft','craft','faccao_roxa','Produção da Facção Roxa',2485.3,1660.5),
 ('mafia_branca_warehouse','warehouse','mafia_branca','Depósito da Máfia Branca',2489.3,1676.5),
 ('mafia_branca_craft','craft','mafia_branca','Produção da Máfia Branca',2493.3,1676.5)
) AS p(code,kind,organization_code,label,x,z)
JOIN organizations o ON o.code=p.organization_code;

CREATE TABLE criminal_activity_sessions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 request_id uuid UNIQUE NOT NULL,
 character_id uuid NOT NULL REFERENCES characters(id),
 activity_code text NOT NULL REFERENCES criminal_activity_definitions(code),
 location_code text NOT NULL REFERENCES criminal_economy_locations(code),
 started_at timestamptz NOT NULL,
 ready_at timestamptz NOT NULL CHECK(ready_at>started_at),
 completed_at timestamptz CHECK(completed_at>=ready_at),
 UNIQUE(id,character_id,activity_code)
);
CREATE UNIQUE INDEX criminal_activity_sessions_one_active ON criminal_activity_sessions(character_id)
 WHERE completed_at IS NULL;
ALTER TABLE criminal_activity_runs ADD COLUMN activity_session_id uuid UNIQUE;
ALTER TABLE criminal_activity_runs ADD CONSTRAINT criminal_activity_runs_session_actor_fk
 FOREIGN KEY(activity_session_id,character_id,activity_code)
 REFERENCES criminal_activity_sessions(id,character_id,activity_code);
CREATE INDEX criminal_activity_sessions_character_time ON criminal_activity_sessions(character_id,started_at DESC);

GRANT SELECT ON criminal_economy_locations TO hbrp_app;
GRANT SELECT,INSERT ON criminal_activity_sessions TO hbrp_app;
GRANT UPDATE(completed_at) ON criminal_activity_sessions TO hbrp_app;
INSERT INTO schema_migrations(version) VALUES(16);
COMMIT;
