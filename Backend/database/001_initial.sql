-- HBRP only. Applied inside the dedicated hbrp database as hbrp_owner.
BEGIN;
CREATE SCHEMA hbrp AUTHORIZATION hbrp_owner;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON SCHEMA hbrp FROM PUBLIC;
SET LOCAL search_path = hbrp, pg_catalog;
ALTER DEFAULT PRIVILEGES FOR ROLE hbrp_owner IN SCHEMA hbrp REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

CREATE TABLE schema_migrations(version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE accounts(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 uid bigint GENERATED ALWAYS AS IDENTITY UNIQUE NOT NULL,
 email varchar(254), status text NOT NULL DEFAULT 'active' CHECK(status IN ('active','suspended','banned','closed')),
 created_at timestamptz NOT NULL DEFAULT now(), last_login_at timestamptz, closed_at timestamptz,
 CHECK(email IS NULL OR (email=btrim(email) AND position('@' IN email)>1))
);
CREATE UNIQUE INDEX accounts_email_unique ON accounts(lower(email)) WHERE email IS NOT NULL;
CREATE TABLE auth_identities(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), account_id uuid NOT NULL REFERENCES accounts(id),
 provider text NOT NULL CHECK(provider IN ('email','google','apple')),
 provider_subject text NOT NULL CHECK(length(provider_subject) BETWEEN 1 AND 512),
 verified_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(provider,provider_subject),UNIQUE(account_id,provider)
);
CREATE INDEX auth_identities_account ON auth_identities(account_id);
CREATE TABLE password_credentials(
 account_id uuid PRIMARY KEY REFERENCES accounts(id), password_hash text NOT NULL CHECK(length(password_hash)>=40),
 changed_at timestamptz NOT NULL DEFAULT now()
);
COMMENT ON COLUMN password_credentials.password_hash IS 'Hash calculado pela API (ex.: Argon2id); nunca senha em texto puro.';
CREATE TABLE sessions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),account_id uuid NOT NULL REFERENCES accounts(id),
 token_hash bytea UNIQUE NOT NULL CHECK(octet_length(token_hash)=32),
 created_at timestamptz NOT NULL DEFAULT now(), expires_at timestamptz NOT NULL,
 revoked_at timestamptz, CHECK(expires_at>created_at)
);
CREATE INDEX sessions_account ON sessions(account_id);

CREATE TABLE worlds(
 id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,code text UNIQUE NOT NULL,
 scene_name text NOT NULL, enabled boolean NOT NULL DEFAULT true
);
INSERT INTO worlds(code,scene_name) VALUES('centro_teste','TestePersonagem');
CREATE TABLE characters(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),account_id uuid NOT NULL REFERENCES accounts(id),
 name varchar(24) NOT NULL CHECK(name=btrim(name) AND length(name)>=3),
 created_at timestamptz NOT NULL DEFAULT now(),deleted_at timestamptz
);
CREATE UNIQUE INDEX character_names_unique ON characters(lower(name));
CREATE INDEX characters_account ON characters(account_id);
CREATE TABLE character_stats(
 character_id uuid PRIMARY KEY REFERENCES characters(id),level integer NOT NULL DEFAULT 1 CHECK(level>=1),
 xp bigint NOT NULL DEFAULT 0 CHECK(xp>=0),health numeric(5,2) NOT NULL DEFAULT 100 CHECK(health BETWEEN 0 AND 100),
 hunger numeric(5,2) NOT NULL DEFAULT 100 CHECK(hunger BETWEEN 0 AND 100),
 thirst numeric(5,2) NOT NULL DEFAULT 100 CHECK(thirst BETWEEN 0 AND 100),
 energy numeric(5,2) NOT NULL DEFAULT 100 CHECK(energy BETWEEN 0 AND 100),
 play_seconds bigint NOT NULL DEFAULT 0 CHECK(play_seconds>=0),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE character_positions(
 character_id uuid PRIMARY KEY REFERENCES characters(id),world_id smallint NOT NULL REFERENCES worlds(id),
 x double precision NOT NULL DEFAULT 0 CHECK(x BETWEEN -1000000 AND 1000000),
 y double precision NOT NULL DEFAULT -.10 CHECK(y BETWEEN -1000000 AND 1000000),
 z double precision NOT NULL DEFAULT 0 CHECK(z BETWEEN -1000000 AND 1000000),
 yaw real NOT NULL DEFAULT 90 CHECK(yaw>=0 AND yaw<360),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE character_appearance(
 character_id uuid PRIMARY KEY REFERENCES characters(id),model_code text NOT NULL DEFAULT 'bybeyby',
 customization jsonb NOT NULL DEFAULT '{}' CHECK(jsonb_typeof(customization)='object' AND octet_length(customization::text)<=32768),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE item_definitions(
 code text PRIMARY KEY,name text NOT NULL,category text NOT NULL,
 max_stack integer NOT NULL DEFAULT 1 CHECK(max_stack BETWEEN 1 AND 9999),
 enabled boolean NOT NULL DEFAULT true,metadata jsonb NOT NULL DEFAULT '{}' CHECK(jsonb_typeof(metadata)='object')
);
CREATE TABLE inventories(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),character_id uuid UNIQUE NOT NULL REFERENCES characters(id),
 capacity integer NOT NULL DEFAULT 20 CHECK(capacity BETWEEN 1 AND 1000)
);
CREATE TABLE inventory_items(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),inventory_id uuid NOT NULL REFERENCES inventories(id),
 item_code text NOT NULL REFERENCES item_definitions(code),slot integer NOT NULL CHECK(slot>=0),
 quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 9999),metadata jsonb NOT NULL DEFAULT '{}',
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),UNIQUE(inventory_id,slot)
);
CREATE FUNCTION validate_inventory_item() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,hbrp AS $$
DECLARE slots integer; stack integer;
BEGIN
 SELECT capacity INTO slots FROM hbrp.inventories WHERE id=NEW.inventory_id FOR UPDATE;
 SELECT max_stack INTO stack FROM hbrp.item_definitions WHERE code=NEW.item_code;
 IF NEW.slot>=slots OR NEW.quantity>stack THEN RAISE EXCEPTION 'Slot ou quantidade acima do limite'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER inventory_limits BEFORE INSERT OR UPDATE ON inventory_items FOR EACH ROW EXECUTE FUNCTION validate_inventory_item();

CREATE TABLE wallets(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),account_id uuid REFERENCES accounts(id),
 character_id uuid REFERENCES characters(id),currency text NOT NULL CHECK(currency IN ('cash','bank','hz')),
 balance bigint NOT NULL DEFAULT 0 CHECK(balance BETWEEN 0 AND 9000000000000000),
 updated_at timestamptz NOT NULL DEFAULT now(),
 CHECK((currency='hz' AND account_id IS NOT NULL AND character_id IS NULL) OR
       (currency IN ('cash','bank') AND character_id IS NOT NULL AND account_id IS NULL))
);
CREATE UNIQUE INDEX account_wallet_unique ON wallets(account_id,currency) WHERE account_id IS NOT NULL;
CREATE UNIQUE INDEX character_wallet_unique ON wallets(character_id,currency) WHERE character_id IS NOT NULL;
COMMENT ON COLUMN wallets.balance IS 'cash/bank em centavos; hz em moedas inteiras. Nunca ponto flutuante.';
CREATE TABLE financial_operations(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),idempotency_key uuid NOT NULL UNIQUE,
 source_wallet uuid REFERENCES wallets(id),destination_wallet uuid REFERENCES wallets(id),
 amount bigint NOT NULL CHECK(amount>0 AND amount<=1000000000000),
 reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 200),created_at timestamptz NOT NULL DEFAULT now(),
 CHECK(source_wallet IS NOT NULL OR destination_wallet IS NOT NULL),
 CHECK(source_wallet IS DISTINCT FROM destination_wallet)
);
CREATE TABLE wallet_entries(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,operation_id uuid NOT NULL REFERENCES financial_operations(id),
 wallet_id uuid NOT NULL REFERENCES wallets(id),delta bigint NOT NULL CHECK(delta<>0),
 balance_after bigint NOT NULL CHECK(balance_after>=0),created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(operation_id,wallet_id)
);
CREATE INDEX wallet_entries_history ON wallet_entries(wallet_id,created_at DESC);

CREATE FUNCTION change_balance(p_wallet uuid,p_delta bigint,p_key uuid,p_reason text) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,hbrp AS $$
DECLARE op uuid; previous hbrp.financial_operations%ROWTYPE; after_balance bigint;
 src uuid; dst uuid;
BEGIN
 IF p_delta IS NULL OR p_delta=0 OR p_delta NOT BETWEEN -1000000000000 AND 1000000000000 THEN RAISE EXCEPTION 'Valor inválido'; END IF;
 IF p_delta<0 THEN src=p_wallet; ELSE dst=p_wallet; END IF;
 INSERT INTO hbrp.financial_operations(idempotency_key,source_wallet,destination_wallet,amount,reason)
 VALUES(p_key,src,dst,abs(p_delta),p_reason) ON CONFLICT(idempotency_key) DO NOTHING RETURNING id INTO op;
 IF op IS NULL THEN
  SELECT * INTO previous FROM hbrp.financial_operations WHERE idempotency_key=p_key;
  IF previous.source_wallet IS DISTINCT FROM src OR previous.destination_wallet IS DISTINCT FROM dst OR previous.amount<>abs(p_delta) OR previous.reason<>p_reason THEN
   RAISE EXCEPTION 'Chave de idempotência reutilizada com dados diferentes';
  END IF;
  RETURN previous.id;
 END IF;
 UPDATE hbrp.wallets SET balance=balance+p_delta,updated_at=now()
 WHERE id=p_wallet AND balance+p_delta BETWEEN 0 AND 9000000000000000 RETURNING balance INTO after_balance;
 IF NOT FOUND THEN RAISE EXCEPTION 'Carteira ausente, saldo insuficiente ou limite excedido'; END IF;
 INSERT INTO hbrp.wallet_entries(operation_id,wallet_id,delta,balance_after) VALUES(op,p_wallet,p_delta,after_balance);
 RETURN op;
END $$;

CREATE FUNCTION transfer_balance(p_from uuid,p_to uuid,p_amount bigint,p_key uuid,p_reason text) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,hbrp AS $$
DECLARE op uuid; previous hbrp.financial_operations%ROWTYPE; from_currency text; to_currency text; from_balance bigint; to_balance bigint;
BEGIN
 IF p_from=p_to OR p_amount IS NULL OR p_amount<=0 OR p_amount>1000000000000 THEN RAISE EXCEPTION 'Transferência inválida'; END IF;
 INSERT INTO hbrp.financial_operations(idempotency_key,source_wallet,destination_wallet,amount,reason)
 VALUES(p_key,p_from,p_to,p_amount,p_reason) ON CONFLICT(idempotency_key) DO NOTHING RETURNING id INTO op;
 IF op IS NULL THEN
  SELECT * INTO previous FROM hbrp.financial_operations WHERE idempotency_key=p_key;
  IF previous.source_wallet IS DISTINCT FROM p_from OR previous.destination_wallet IS DISTINCT FROM p_to OR previous.amount<>p_amount OR previous.reason<>p_reason THEN
   RAISE EXCEPTION 'Chave de idempotência reutilizada com dados diferentes';
  END IF;
  RETURN previous.id;
 END IF;
 PERFORM id FROM hbrp.wallets WHERE id IN(p_from,p_to) ORDER BY id FOR UPDATE;
 SELECT currency,balance INTO from_currency,from_balance FROM hbrp.wallets WHERE id=p_from;
 SELECT currency,balance INTO to_currency,to_balance FROM hbrp.wallets WHERE id=p_to;
 IF from_currency IS NULL OR to_currency IS NULL OR ((from_currency='hz')<>(to_currency='hz')) THEN RAISE EXCEPTION 'Moedas incompatíveis'; END IF;
 IF from_balance<p_amount THEN RAISE EXCEPTION 'Saldo insuficiente'; END IF;
 UPDATE hbrp.wallets SET balance=balance-p_amount,updated_at=now() WHERE id=p_from;
 UPDATE hbrp.wallets SET balance=balance+p_amount,updated_at=now() WHERE id=p_to;
 INSERT INTO hbrp.wallet_entries(operation_id,wallet_id,delta,balance_after)
 VALUES(op,p_from,-p_amount,from_balance-p_amount),(op,p_to,p_amount,to_balance+p_amount);
 RETURN op;
END $$;

CREATE TABLE vehicle_definitions(code text PRIMARY KEY,name text NOT NULL,metadata jsonb NOT NULL DEFAULT '{}');
CREATE TABLE player_vehicles(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),character_id uuid NOT NULL REFERENCES characters(id),
 model_code text NOT NULL REFERENCES vehicle_definitions(code),plate varchar(12) UNIQUE NOT NULL,
 fuel numeric(5,2) NOT NULL DEFAULT 100 CHECK(fuel BETWEEN 0 AND 100),
 health numeric(5,2) NOT NULL DEFAULT 100 CHECK(health BETWEEN 0 AND 100),
 garage_code text,customization jsonb NOT NULL DEFAULT '{}',created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX vehicles_owner ON player_vehicles(character_id);
CREATE TABLE property_definitions(code text PRIMARY KEY,name text NOT NULL,kind text NOT NULL CHECK(kind IN ('house','hotel','business')));
CREATE TABLE player_properties(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),property_code text UNIQUE NOT NULL REFERENCES property_definitions(code),
 character_id uuid NOT NULL REFERENCES characters(id),acquired_at timestamptz NOT NULL DEFAULT now(),lease_expires_at timestamptz
);
CREATE INDEX properties_owner ON player_properties(character_id);
CREATE TABLE organizations(id uuid PRIMARY KEY DEFAULT gen_random_uuid(),code text UNIQUE NOT NULL,name text NOT NULL,kind text NOT NULL CHECK(kind IN ('public','faction','company')));
CREATE TABLE organization_members(
 organization_id uuid NOT NULL REFERENCES organizations(id),character_id uuid NOT NULL REFERENCES characters(id),
 rank_code text NOT NULL,joined_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(organization_id,character_id)
);
CREATE INDEX organization_members_character ON organization_members(character_id);
CREATE TABLE character_progress(
 character_id uuid NOT NULL REFERENCES characters(id),category text NOT NULL CHECK(category IN ('job','mission','skill','tutorial')),
 code text NOT NULL,state jsonb NOT NULL DEFAULT '{}',version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 updated_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(character_id,category,code)
);
CREATE TABLE entitlements(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),account_id uuid NOT NULL REFERENCES accounts(id),
 code text NOT NULL,source_reference text UNIQUE NOT NULL,starts_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz,revoked_at timestamptz,CHECK(expires_at IS NULL OR expires_at>starts_at)
);
CREATE INDEX entitlements_account ON entitlements(account_id);
CREATE TABLE sanctions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),account_id uuid NOT NULL REFERENCES accounts(id),
 kind text NOT NULL CHECK(kind IN ('warning','mute','suspension','ban')),reason text NOT NULL,
 created_by uuid REFERENCES accounts(id),created_at timestamptz NOT NULL DEFAULT now(),expires_at timestamptz,revoked_at timestamptz
);
CREATE INDEX sanctions_account ON sanctions(account_id);
CREATE TABLE audit_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,actor_account_id uuid REFERENCES accounts(id),
 action text NOT NULL,entity_type text NOT NULL,entity_id text,details jsonb NOT NULL DEFAULT '{}',created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX audit_events_time ON audit_events(created_at DESC);

GRANT USAGE ON SCHEMA hbrp TO hbrp_app;
GRANT SELECT ON ALL TABLES IN SCHEMA hbrp TO hbrp_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA hbrp TO hbrp_app;
GRANT INSERT,UPDATE ON auth_identities,password_credentials,sessions,characters,character_stats,
 character_positions,character_appearance,inventories,inventory_items,player_vehicles,player_properties,
 organization_members,character_progress TO hbrp_app;
GRANT DELETE ON sessions,inventory_items,organization_members TO hbrp_app;
GRANT INSERT(id,email,status,created_at,last_login_at,closed_at) ON accounts TO hbrp_app;
GRANT UPDATE(email,status,last_login_at,closed_at) ON accounts TO hbrp_app;
GRANT INSERT(id,account_id,character_id,currency) ON wallets TO hbrp_app;
GRANT INSERT ON audit_events TO hbrp_app;
-- UID uses GENERATED ALWAYS; the API must never supply/replace it.
REVOKE ALL ON FUNCTION change_balance(uuid,bigint,uuid,text) FROM PUBLIC;
REVOKE ALL ON FUNCTION transfer_balance(uuid,uuid,bigint,uuid,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION change_balance(uuid,bigint,uuid,text),transfer_balance(uuid,uuid,bigint,uuid,text) TO hbrp_app;
INSERT INTO schema_migrations(version) VALUES(1);
COMMIT;
