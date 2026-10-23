BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
CREATE UNIQUE INDEX one_active_character_per_account ON characters(account_id) WHERE deleted_at IS NULL;
ALTER TABLE character_positions ADD COLUMN camera_yaw real NOT NULL DEFAULT 90 CHECK(camera_yaw>=0 AND camera_yaw<360);
ALTER TABLE character_positions ADD COLUMN camera_pitch real NOT NULL DEFAULT 16 CHECK(camera_pitch BETWEEN -10 AND 65);
ALTER TABLE sessions ADD COLUMN last_checkpoint_at timestamptz NOT NULL DEFAULT now();
CREATE TABLE save_requests(
 session_id uuid NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
 request_id uuid NOT NULL,character_id uuid NOT NULL REFERENCES characters(id),
 payload_hash bytea NOT NULL CHECK(octet_length(payload_hash)=32),
 resulting_version bigint NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(session_id,request_id)
);
GRANT SELECT,INSERT ON save_requests TO hbrp_app;
INSERT INTO schema_migrations(version) VALUES(3);
COMMIT;
