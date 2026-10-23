BEGIN;
SET LOCAL ROLE hbrp_owner;
SET LOCAL search_path=hbrp,pg_catalog;
CREATE OR REPLACE FUNCTION transfer_balance(p_from uuid,p_to uuid,p_amount bigint,p_key uuid,p_reason text) RETURNS uuid
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
 PERFORM id FROM hbrp.wallets WHERE id IN(p_from,p_to) ORDER BY id FOR NO KEY UPDATE;
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
INSERT INTO schema_migrations(version) VALUES(2);
COMMIT;
