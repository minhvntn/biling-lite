-- AlterTable
ALTER TABLE "public"."pc_groups"
ADD COLUMN IF NOT EXISTS "member_hourly_rate" DECIMAL(12, 2) NOT NULL DEFAULT 5000;

-- Backfill existing groups so member rate starts from current group hourly rate.
UPDATE "public"."pc_groups"
SET "member_hourly_rate" = "hourly_rate"
WHERE "member_hourly_rate" IS NULL OR "member_hourly_rate" = 5000;
