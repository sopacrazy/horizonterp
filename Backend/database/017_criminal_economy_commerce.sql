-- Player-supplied stores and recoverable activity lifecycle. No stock or money is seeded.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
ALTER TABLE criminal_activity_sessions ADD COLUMN cancelled_at timestamptz;
ALTER TABLE criminal_activity_sessions ADD COLUMN expires_at timestamptz;
UPDATE criminal_activity_sessions SET expires_at=ready_at+interval '15 minutes';
ALTER TABLE criminal_activity_sessions ALTER COLUMN expires_at SET NOT NULL;
DROP INDEX criminal_activity_sessions_one_active;
CREATE UNIQUE INDEX criminal_activity_sessions_one_active ON criminal_activity_sessions(character_id)
 WHERE completed_at IS NULL AND cancelled_at IS NULL;
GRANT INSERT(expires_at),UPDATE(cancelled_at) ON criminal_activity_sessions TO hbrp_app;

CREATE TABLE criminal_shops(
 code text PRIMARY KEY REFERENCES criminal_economy_locations(code),
 organization_id uuid UNIQUE NOT NULL REFERENCES organizations(id),
 proceeds bigint NOT NULL DEFAULT 0 CHECK(proceeds BETWEEN 0 AND 1000000000000)
);
CREATE TABLE criminal_shop_stock(
 shop_code text NOT NULL REFERENCES criminal_shops(code),item_code text NOT NULL REFERENCES item_definitions(code),
 quantity integer NOT NULL CHECK(quantity BETWEEN 0 AND 1000000),
 price_cents integer NOT NULL CHECK(price_cents BETWEEN 100 AND 100000000),
 PRIMARY KEY(shop_code,item_code)
);
CREATE TABLE criminal_commerce_operations(
 request_id uuid PRIMARY KEY,character_id uuid NOT NULL REFERENCES characters(id),
 organization_id uuid REFERENCES organizations(id),operation text NOT NULL,
 payload jsonb NOT NULL,result jsonb NOT NULL,created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX criminal_commerce_actor_time ON criminal_commerce_operations(character_id,created_at DESC);
CREATE INDEX criminal_commerce_org_time ON criminal_commerce_operations(organization_id,created_at DESC);
ALTER TABLE criminal_economy_locations DROP CONSTRAINT criminal_economy_locations_kind_check;
ALTER TABLE criminal_economy_locations DROP CONSTRAINT criminal_economy_locations_check;
ALTER TABLE criminal_economy_locations ADD CHECK(kind IN ('activity','warehouse','craft','shop'));
ALTER TABLE criminal_economy_locations ADD CHECK(
 (kind='activity' AND activity_code IS NOT NULL AND organization_id IS NULL) OR
 (kind IN ('warehouse','craft','shop') AND activity_code IS NULL AND organization_id IS NOT NULL));
INSERT INTO criminal_economy_locations(code,kind,organization_id,world,label,x,y,z,radius)
SELECT o.code||'_shop','shop',o.id,'centro_teste','Loja • '||o.name,
 CASE WHEN o.code='faccao_roxa' THEN 2477.3 ELSE 2501.3 END,12.37,1676.5,4
FROM organizations o WHERE o.code IN ('faccao_roxa','mafia_branca');
INSERT INTO criminal_shops(code,organization_id)
SELECT code,organization_id FROM criminal_economy_locations WHERE kind='shop';
GRANT SELECT,UPDATE ON criminal_shops TO hbrp_app;
GRANT SELECT,INSERT,UPDATE ON criminal_shop_stock TO hbrp_app;
GRANT SELECT,INSERT ON criminal_commerce_operations TO hbrp_app;
-- Row locking for purchases; balance changes remain restricted to the ledger function.
GRANT UPDATE(updated_at) ON wallets TO hbrp_app;
INSERT INTO schema_migrations(version) VALUES(17);
COMMIT;
