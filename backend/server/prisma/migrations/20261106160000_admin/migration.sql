-- Admin web: account bans and the admin audit log.
ALTER TABLE "accounts" ADD COLUMN "banned_at" TIMESTAMP(3);
ALTER TABLE "accounts" ADD COLUMN "ban_reason" TEXT;

CREATE TABLE "admin_audit" (
    "id" TEXT NOT NULL,
    "actor" TEXT NOT NULL,
    "action" TEXT NOT NULL,
    "target_type" TEXT NOT NULL,
    "target_id" TEXT NOT NULL,
    "reason" TEXT NOT NULL,
    "details" JSONB NOT NULL DEFAULT '{}',
    "at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "admin_audit_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "admin_audit_at_idx" ON "admin_audit"("at");
CREATE INDEX "admin_audit_target_type_target_id_at_idx" ON "admin_audit"("target_type", "target_id", "at");
