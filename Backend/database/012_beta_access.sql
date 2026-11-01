-- Closed beta access is an explicit grant, separate from preserved legacy accounts.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
CREATE TABLE beta_access(
 account_id uuid PRIMARY KEY REFERENCES accounts(id),
 label text NOT NULL CHECK(length(label) BETWEEN 1 AND 64 AND label=btrim(label)),
 password_hash text NOT NULL CHECK(length(password_hash)>=40),
 credential_version bigint NOT NULL DEFAULT 1 CHECK(credential_version>0),
 can_manage boolean NOT NULL DEFAULT false,
 approved_by uuid REFERENCES accounts(id),
 created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 revoked_at timestamptz,revoked_reason text NOT NULL DEFAULT '' CHECK(length(revoked_reason)<=180)
);
ALTER TABLE sessions ADD COLUMN beta_access_version bigint CHECK(beta_access_version IS NULL OR beta_access_version>0);
CREATE TABLE beta_access_requests(
 actor_account_id uuid NOT NULL REFERENCES accounts(id),request_id uuid NOT NULL,
 action text NOT NULL CHECK(action IN ('create','approve','reset','revoke','bootstrap_batch')),
 account_id uuid REFERENCES accounts(id),payload_hash bytea NOT NULL CHECK(octet_length(payload_hash)=32),
 result jsonb NOT NULL CHECK(jsonb_typeof(result)='object' AND octet_length(result::text)<=4096),
 created_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(actor_account_id,request_id)
);
COMMENT ON COLUMN beta_access.password_hash IS 'Panel-issued scrypt hash. No readable passwords are retained.';
COMMENT ON COLUMN beta_access.can_manage IS 'Explicit operator-issued beta-management capability; never editable by public API bodies.';
COMMENT ON COLUMN sessions.beta_access_version IS 'Closed-beta sessions must match the currently active issued credential version.';
COMMENT ON TABLE beta_access_requests IS 'Idempotency metadata only; never passwords or tokens.';
GRANT SELECT,INSERT,UPDATE ON beta_access TO hbrp_app;
GRANT SELECT,INSERT ON beta_access_requests TO hbrp_app;
INSERT INTO schema_migrations(version) VALUES(12);
COMMIT;
