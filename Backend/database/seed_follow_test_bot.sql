-- Run after migration 008. One non-login follower for the test player with UID 18.
BEGIN;
SET LOCAL ROLE hbrp_owner;
DO $seed$
DECLARE owner_id uuid; bot_account uuid; bot_character uuid;
BEGIN
 SELECT c.id INTO owner_id
 FROM hbrp.accounts a JOIN hbrp.characters c ON c.account_id=a.id AND c.deleted_at IS NULL
 JOIN hbrp.character_positions p ON p.character_id=c.id
 WHERE a.uid=18 AND a.status='active';
 IF owner_id IS NULL THEN RAISE EXCEPTION 'Test player UID 18 is missing'; END IF;
 IF EXISTS(SELECT 1 FROM hbrp.follow_test_bots WHERE owner_character_id=owner_id) THEN RETURN; END IF;
 IF EXISTS(SELECT 1 FROM hbrp.characters WHERE lower(name)=lower('Bot Parceiro'))
  THEN RAISE EXCEPTION 'Bot name is already in use'; END IF;
 INSERT INTO hbrp.accounts(status,account_role,admin_level)
 VALUES('active','user',0) RETURNING id INTO bot_account;
 INSERT INTO hbrp.characters(account_id,name,first_name,last_name,gender)
 VALUES(bot_account,'Bot Parceiro','Bot','Parceiro','male') RETURNING id INTO bot_character;
 INSERT INTO hbrp.character_stats(character_id) VALUES(bot_character);
 INSERT INTO hbrp.character_positions(character_id,world_id,x,y,z,yaw,camera_yaw,camera_pitch)
 SELECT bot_character,p.world_id,p.x,p.y,p.z,p.yaw,p.camera_yaw,p.camera_pitch
 FROM hbrp.character_positions p WHERE p.character_id=owner_id;
 INSERT INTO hbrp.character_appearance(character_id,model_code) VALUES(bot_character,'bybeyby');
 INSERT INTO hbrp.wallets(character_id,currency) VALUES(bot_character,'cash'),(bot_character,'bank');
 INSERT INTO hbrp.wallets(account_id,currency) VALUES(bot_account,'hz');
 INSERT INTO hbrp.follow_test_bots(owner_character_id,bot_character_id)
 VALUES(owner_id,bot_character);
END $seed$;
COMMIT;
