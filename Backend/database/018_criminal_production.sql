-- Exclusive fictional RP catalog and persistent timed manufacturing. No memberships or stock are seeded.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
INSERT INTO organizations(code,name,kind) VALUES
 ('faccao_verde','Facção Verde','faction'),('faccao_vermelha','Facção Vermelha','faction'),
 ('mafia_laranja','Máfia Laranja','faction'),('mafia_preta','Máfia Preta','faction') ON CONFLICT(code) DO NOTHING;
INSERT INTO organization_warehouses(organization_id)
SELECT id FROM organizations WHERE code IN ('faccao_verde','faccao_vermelha','mafia_laranja','mafia_preta') ON CONFLICT DO NOTHING;
INSERT INTO organization_economy_rank_permissions(organization_id,rank_code,can_deposit,can_withdraw,can_craft,max_operation_quantity)
SELECT o.id,p.rank_code,p.can_deposit,p.can_withdraw,p.can_craft,p.max_operation_quantity
FROM organizations o CROSS JOIN organization_economy_rank_permissions p
JOIN organizations source ON source.id=p.organization_id AND source.code='faccao_roxa'
WHERE o.code IN ('faccao_verde','faccao_vermelha','mafia_laranja','mafia_preta') ON CONFLICT DO NOTHING;
INSERT INTO criminal_economy_locations(code,kind,organization_id,world,label,x,y,z,radius)
SELECT o.code||'_'||p.kind,p.kind,o.id,'centro_teste',o.name||' • '||p.caption,v.x+p.dx,12.37,v.z,4
FROM (VALUES ('faccao_verde',2465.3,1644.5),('faccao_vermelha',2513.3,1644.5),
 ('mafia_laranja',2465.3,1692.5),('mafia_preta',2513.3,1692.5)) v(code,x,z)
JOIN organizations o ON o.code=v.code
CROSS JOIN (VALUES ('warehouse','Depósito',0),('craft','Bancada',-8),('shop','Loja',8)) p(kind,caption,dx);
INSERT INTO criminal_shops(code,organization_id)
SELECT code,organization_id FROM criminal_economy_locations WHERE kind='shop' ON CONFLICT DO NOTHING;
ALTER TABLE criminal_recipe_definitions ADD COLUMN duration_seconds integer NOT NULL DEFAULT 30 CHECK(duration_seconds BETWEEN 5 AND 3600);
INSERT INTO item_definitions(code,name,category,max_stack,enabled,metadata) VALUES
 ('rp_parafal','Parafal (RP)','pilot_product',20,true,'{"criminalEconomyPilot":true,"fictional":true,"weaponCatalog":"parafal"}'),
 ('rp_ak47','AK-47 (RP)','pilot_product',20,true,'{"criminalEconomyPilot":true,"fictional":true,"weaponCatalog":"ak47"}'),
 ('rp_m4','M4 (RP)','pilot_product',20,true,'{"criminalEconomyPilot":true,"fictional":true,"weaponCatalog":"m4"}'),
 ('rp_ammo_parafal','Munição de Parafal (RP)','pilot_product',100,true,'{"criminalEconomyPilot":true,"fictional":true,"ammoCatalog":"parafal"}'),
 ('rp_ammo_ak47','Munição de AK-47 (RP)','pilot_product',100,true,'{"criminalEconomyPilot":true,"fictional":true,"ammoCatalog":"ak47"}'),
 ('rp_ammo_m4','Munição de M4 (RP)','pilot_product',100,true,'{"criminalEconomyPilot":true,"fictional":true,"ammoCatalog":"m4"}');
INSERT INTO criminal_recipe_definitions(code,name,output_item_code,output_quantity,duration_seconds,metadata)
SELECT v.code,'Produção RP • '||v.name,v.code,v.quantity,v.seconds,'{"fictional":true,"abstractGameplayRecipe":true}'::jsonb
FROM (VALUES ('rp_parafal','Parafal',1,60),('rp_ak47','AK-47',1,60),('rp_m4','M4',1,60),
 ('rp_ammo_parafal','Munição de Parafal',30,30),('rp_ammo_ak47','Munição de AK-47',30,30),('rp_ammo_m4','Munição de M4',30,30)) v(code,name,quantity,seconds);
INSERT INTO criminal_recipe_ingredients(recipe_code,item_code,quantity)
SELECT code,'pilot_scrap',CASE WHEN output_quantity=1 THEN 6 ELSE 3 END FROM criminal_recipe_definitions WHERE code LIKE 'rp_%'
UNION ALL SELECT code,'pilot_crystal',CASE WHEN output_quantity=1 THEN 4 ELSE 2 END FROM criminal_recipe_definitions WHERE code LIKE 'rp_%';
INSERT INTO criminal_recipe_organizations(recipe_code,organization_id)
SELECT v.recipe,o.id FROM (VALUES ('rp_parafal','faccao_roxa'),('rp_ak47','faccao_verde'),('rp_m4','faccao_vermelha'),
 ('rp_ammo_parafal','mafia_branca'),('rp_ammo_ak47','mafia_laranja'),('rp_ammo_m4','mafia_preta')) v(recipe,organization)
JOIN organizations o ON o.code=v.organization;
CREATE TABLE criminal_production_jobs(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),request_id uuid UNIQUE NOT NULL,
 organization_id uuid NOT NULL REFERENCES organizations(id),character_id uuid NOT NULL REFERENCES characters(id),
 recipe_code text NOT NULL REFERENCES criminal_recipe_definitions(code),batches integer NOT NULL CHECK(batches BETWEEN 1 AND 1000),
 location_code text NOT NULL REFERENCES criminal_economy_locations(code),
 output_item_code text NOT NULL REFERENCES item_definitions(code),output_quantity integer NOT NULL CHECK(output_quantity>0),
 ingredients jsonb NOT NULL CHECK(jsonb_typeof(ingredients)='array'),
 started_at timestamptz NOT NULL,ready_at timestamptz NOT NULL CHECK(ready_at>started_at),
 closed_at timestamptz,status text NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','completed','cancelled')),
 CHECK((status='pending' AND closed_at IS NULL) OR (status<>'pending' AND closed_at IS NOT NULL))
);
CREATE UNIQUE INDEX criminal_production_one_pending ON criminal_production_jobs(organization_id) WHERE status='pending';
ALTER TABLE organization_warehouse_operations DROP CONSTRAINT organization_warehouse_operations_operation_type_check;
ALTER TABLE organization_warehouse_operations ADD CHECK(operation_type IN ('deposit','withdraw','craft','production_start','production_cancel'));
GRANT SELECT,INSERT ON criminal_production_jobs TO hbrp_app;
GRANT UPDATE(status,closed_at) ON criminal_production_jobs TO hbrp_app;
INSERT INTO schema_migrations(version) VALUES(18);
COMMIT;
