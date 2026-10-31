BEGIN;
SET LOCAL ROLE hbrp_owner;
CREATE TABLE hbrp.police_tickets (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 character_id uuid NOT NULL REFERENCES hbrp.characters(id),
 officer_account_id uuid NOT NULL REFERENCES hbrp.accounts(id),
 amount_cents bigint NOT NULL CHECK(amount_cents BETWEEN 100 AND 100000000),
 reason varchar(180) NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 180),
 status text NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','paid','cancelled')),
 created_at timestamptz NOT NULL DEFAULT now(),
 paid_at timestamptz
);
CREATE INDEX police_tickets_character ON hbrp.police_tickets(character_id,created_at DESC);
CREATE TABLE hbrp.police_calls (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 caller_character_id uuid NOT NULL REFERENCES hbrp.characters(id),
 message varchar(180) NOT NULL CHECK(length(btrim(message)) BETWEEN 3 AND 180),
 status text NOT NULL DEFAULT 'open' CHECK(status IN ('open','accepted','closed')),
 officer_account_id uuid REFERENCES hbrp.accounts(id),
 created_at timestamptz NOT NULL DEFAULT now(),
 accepted_at timestamptz
);
CREATE INDEX police_calls_open_caller ON hbrp.police_calls(caller_character_id,created_at DESC) WHERE status='open';
GRANT SELECT,INSERT,UPDATE ON hbrp.police_tickets,hbrp.police_calls TO hbrp_app;
INSERT INTO hbrp.schema_migrations(version) VALUES(10);
COMMIT;
