-- Additive local pilot for a server-authoritative criminal economy.
-- The items and recipe are intentionally fictional; no real-world manufacturing data is stored.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;

INSERT INTO organizations(code,name,kind) VALUES
 ('faccao_roxa','Facção Roxa','faction'),
 ('mafia_branca','Máfia Branca','faction')
ON CONFLICT(code) DO NOTHING;

INSERT INTO item_definitions(code,name,category,max_stack,enabled,metadata) VALUES
 ('pilot_scrap','Sucata de teste','pilot_material',100,true,
  '{"criminalEconomyPilot":true,"fictional":true,"source":"urban_recycling"}'::jsonb),
 ('pilot_crystal','Cristal de teste','pilot_material',100,true,
  '{"criminalEconomyPilot":true,"fictional":true,"source":"test_mining"}'::jsonb),
 ('pilot_logistics_crate','Caixa logística de teste','pilot_product',20,true,
  '{"criminalEconomyPilot":true,"fictional":true,"tradeable":false}'::jsonb)
ON CONFLICT(code) DO NOTHING;

CREATE TABLE criminal_activity_definitions(
 code text PRIMARY KEY CHECK(code ~ '^[a-z][a-z0-9_]{2,47}$'),
 name text NOT NULL CHECK(length(btrim(name)) BETWEEN 3 AND 80),
 reward_item_code text NOT NULL REFERENCES item_definitions(code),
 reward_quantity integer NOT NULL CHECK(reward_quantity BETWEEN 1 AND 100),
 cooldown_seconds integer NOT NULL CHECK(cooldown_seconds BETWEEN 5 AND 86400),
 enabled boolean NOT NULL DEFAULT true,
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb
  CHECK(jsonb_typeof(metadata)='object' AND octet_length(metadata::text)<=4096)
);

INSERT INTO criminal_activity_definitions
 (code,name,reward_item_code,reward_quantity,cooldown_seconds,metadata) VALUES
 ('urban_recycling','Coleta urbana de teste','pilot_scrap',3,20,
  '{"kind":"garbage","pilot":true}'::jsonb),
 ('test_mining','Mineração de teste','pilot_crystal',2,30,
  '{"kind":"mining","pilot":true}'::jsonb);

CREATE TABLE criminal_activity_runs(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 request_id uuid UNIQUE NOT NULL,
 character_id uuid NOT NULL REFERENCES characters(id),
 activity_code text NOT NULL REFERENCES criminal_activity_definitions(code),
 reward_item_code text NOT NULL REFERENCES item_definitions(code),
 reward_quantity integer NOT NULL CHECK(reward_quantity BETWEEN 1 AND 100),
 result jsonb NOT NULL CHECK(jsonb_typeof(result)='object' AND octet_length(result::text)<=4096),
 completed_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX criminal_activity_runs_cooldown
 ON criminal_activity_runs(character_id,activity_code,completed_at DESC);

CREATE TABLE organization_warehouses(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 organization_id uuid UNIQUE NOT NULL REFERENCES organizations(id),
 capacity_units bigint NOT NULL DEFAULT 5000 CHECK(capacity_units BETWEEN 1 AND 1000000),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 CONSTRAINT organization_warehouses_id_organization_unique UNIQUE(id,organization_id)
);

INSERT INTO organization_warehouses(organization_id,capacity_units)
SELECT id,5000 FROM organizations WHERE code IN ('faccao_roxa','mafia_branca')
ON CONFLICT(organization_id) DO NOTHING;

CREATE TABLE organization_warehouse_items(
 warehouse_id uuid NOT NULL REFERENCES organization_warehouses(id),
 item_code text NOT NULL REFERENCES item_definitions(code),
 quantity bigint NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(warehouse_id,item_code)
);

CREATE TABLE organization_economy_rank_permissions(
 organization_id uuid NOT NULL REFERENCES organizations(id),
 rank_code text NOT NULL CHECK(rank_code ~ '^[a-z][a-z0-9_]{1,31}$'),
 can_deposit boolean NOT NULL DEFAULT true,
 can_withdraw boolean NOT NULL DEFAULT false,
 can_craft boolean NOT NULL DEFAULT false,
 max_operation_quantity integer NOT NULL DEFAULT 100 CHECK(max_operation_quantity BETWEEN 1 AND 10000),
 PRIMARY KEY(organization_id,rank_code)
);

INSERT INTO organization_economy_rank_permissions
 (organization_id,rank_code,can_deposit,can_withdraw,can_craft,max_operation_quantity)
SELECT id,permission.rank_code,permission.can_deposit,permission.can_withdraw,
 permission.can_craft,permission.max_quantity
FROM organizations
CROSS JOIN (VALUES
 ('lider',true,true,true,1000),
 ('sublider',true,true,true,500),
 ('gerente',true,true,true,250),
 ('membro',true,false,false,100),
 ('novato',true,false,false,50)
) AS permission(rank_code,can_deposit,can_withdraw,can_craft,max_quantity)
WHERE code IN ('faccao_roxa','mafia_branca')
ON CONFLICT(organization_id,rank_code) DO NOTHING;

CREATE TABLE criminal_recipe_definitions(
 code text PRIMARY KEY CHECK(code ~ '^[a-z][a-z0-9_]{2,47}$'),
 name text NOT NULL CHECK(length(btrim(name)) BETWEEN 3 AND 80),
 output_item_code text NOT NULL REFERENCES item_definitions(code),
 output_quantity integer NOT NULL CHECK(output_quantity BETWEEN 1 AND 100),
 enabled boolean NOT NULL DEFAULT true,
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb
  CHECK(jsonb_typeof(metadata)='object' AND octet_length(metadata::text)<=4096)
);

CREATE TABLE criminal_recipe_ingredients(
 recipe_code text NOT NULL REFERENCES criminal_recipe_definitions(code),
 item_code text NOT NULL REFERENCES item_definitions(code),
 quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 1000),
 PRIMARY KEY(recipe_code,item_code)
);

CREATE TABLE criminal_recipe_organizations(
 recipe_code text NOT NULL REFERENCES criminal_recipe_definitions(code),
 organization_id uuid NOT NULL REFERENCES organizations(id),
 PRIMARY KEY(recipe_code,organization_id)
);

INSERT INTO criminal_recipe_definitions
 (code,name,output_item_code,output_quantity,metadata) VALUES
 ('pilot_logistics_package','Montagem logística fictícia','pilot_logistics_crate',1,
  '{"pilot":true,"fictional":true,"description":"Receita abstrata de validação econômica"}'::jsonb);
INSERT INTO criminal_recipe_ingredients(recipe_code,item_code,quantity) VALUES
 ('pilot_logistics_package','pilot_scrap',6),
 ('pilot_logistics_package','pilot_crystal',4);
INSERT INTO criminal_recipe_organizations(recipe_code,organization_id)
SELECT 'pilot_logistics_package',id FROM organizations
WHERE code IN ('faccao_roxa','mafia_branca');

CREATE TABLE organization_warehouse_operations(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 request_id uuid UNIQUE NOT NULL,
 warehouse_id uuid NOT NULL,
 organization_id uuid NOT NULL REFERENCES organizations(id),
 character_id uuid NOT NULL REFERENCES characters(id),
 actor_account_id uuid NOT NULL REFERENCES accounts(id),
 operation_type text NOT NULL CHECK(operation_type IN ('deposit','withdraw','craft')),
 request_payload jsonb NOT NULL
  CHECK(jsonb_typeof(request_payload)='object' AND octet_length(request_payload::text)<=4096),
 result jsonb NOT NULL DEFAULT '{}'::jsonb
  CHECK(jsonb_typeof(result)='object' AND octet_length(result::text)<=4096),
 created_at timestamptz NOT NULL DEFAULT now(),
 CONSTRAINT organization_warehouse_operations_warehouse_organization_fk
  FOREIGN KEY(warehouse_id,organization_id) REFERENCES organization_warehouses(id,organization_id),
 CONSTRAINT organization_warehouse_operations_id_warehouse_unique UNIQUE(id,warehouse_id),
 CONSTRAINT organization_warehouse_operations_id_organization_character_unique
  UNIQUE(id,organization_id,character_id)
);
CREATE INDEX organization_warehouse_operations_time
 ON organization_warehouse_operations(warehouse_id,created_at DESC);

CREATE TABLE organization_warehouse_movements(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 operation_id uuid NOT NULL,
 warehouse_id uuid NOT NULL REFERENCES organization_warehouses(id),
 item_code text NOT NULL REFERENCES item_definitions(code),
 quantity_delta bigint NOT NULL CHECK(quantity_delta<>0 AND quantity_delta BETWEEN -1000000 AND 1000000),
 balance_after bigint NOT NULL CHECK(balance_after BETWEEN 0 AND 1000000),
 created_at timestamptz NOT NULL DEFAULT now(),
 CONSTRAINT organization_warehouse_movements_operation_warehouse_fk
  FOREIGN KEY(operation_id,warehouse_id) REFERENCES organization_warehouse_operations(id,warehouse_id)
);
CREATE INDEX organization_warehouse_movements_time
 ON organization_warehouse_movements(warehouse_id,created_at DESC);

CREATE TABLE criminal_production_runs(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 operation_id uuid UNIQUE NOT NULL,
 organization_id uuid NOT NULL,
 character_id uuid NOT NULL,
 recipe_code text NOT NULL REFERENCES criminal_recipe_definitions(code),
 batches integer NOT NULL CHECK(batches BETWEEN 1 AND 1000),
 output_item_code text NOT NULL REFERENCES item_definitions(code),
 output_quantity integer NOT NULL CHECK(output_quantity BETWEEN 1 AND 100000),
 completed_at timestamptz NOT NULL DEFAULT now(),
 CONSTRAINT criminal_production_runs_operation_organization_character_fk
  FOREIGN KEY(operation_id,organization_id,character_id)
  REFERENCES organization_warehouse_operations(id,organization_id,character_id)
);
CREATE INDEX criminal_production_runs_organization
 ON criminal_production_runs(organization_id,completed_at DESC);

COMMENT ON TABLE criminal_activity_definitions IS
 'Server-authoritative fictional collection activities for the local criminal-economy pilot.';
COMMENT ON TABLE organization_warehouse_movements IS
 'Immutable item-level ledger for audited organization warehouse changes.';
COMMENT ON TABLE criminal_recipe_definitions IS
 'Abstract in-game recipes only; no real-world manufacturing instructions.';

GRANT SELECT ON criminal_activity_definitions,criminal_recipe_definitions,
 criminal_recipe_ingredients,criminal_recipe_organizations,
 organization_economy_rank_permissions TO hbrp_app;
-- Runtime ledgers are append-only for the application role. Explicit revokes
-- keep that invariant visible and protect it from broader future grants.
REVOKE UPDATE,DELETE,TRUNCATE ON criminal_activity_runs,organization_warehouse_movements,
 criminal_production_runs,organization_warehouse_operations FROM hbrp_app;
GRANT SELECT,INSERT ON criminal_activity_runs,organization_warehouse_movements,
 criminal_production_runs,organization_warehouse_operations TO hbrp_app;
GRANT SELECT,UPDATE ON organization_warehouses TO hbrp_app;
GRANT SELECT,INSERT,UPDATE,DELETE ON organization_warehouse_items TO hbrp_app;

INSERT INTO schema_migrations(version) VALUES(15);
COMMIT;
