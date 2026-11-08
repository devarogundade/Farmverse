-- Who called the receipt that emitted each event. A market sale's NFT transfer is called by the
-- market contract but carries no authorized_id, so the caller is what tells a sale from a send.
ALTER TABLE chain.events ADD COLUMN predecessor_id TEXT;
