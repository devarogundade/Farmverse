-- Every wallet gets an opaque game uid: 'u' + the first 39 hex characters of sha256(account_id).
-- NEAR account ids contain dots, which game uids and document paths don't allow.
-- Must match uidFor() in src/auth/sessions.ts.
ALTER TABLE "accounts" ADD COLUMN "uid" TEXT;
UPDATE "accounts" SET "uid" = 'u' || substr(encode(sha256(convert_to("account_id", 'UTF8')), 'hex'), 1, 39);
ALTER TABLE "accounts" ALTER COLUMN "uid" SET NOT NULL;
CREATE UNIQUE INDEX "accounts_uid_key" ON "accounts"("uid");
