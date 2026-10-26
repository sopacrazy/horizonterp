-- Optional expiry for chat-issued bans. NULL keeps the existing permanent block.
BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
ALTER TABLE characters ADD COLUMN blocked_until timestamptz;
CREATE INDEX characters_active_block_idx ON characters(blocked_until)
 WHERE blocked_at IS NOT NULL;
INSERT INTO schema_migrations(version) VALUES(7);
COMMIT;
