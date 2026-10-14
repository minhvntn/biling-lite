-- AlterTable
ALTER TABLE "public"."members"
ADD COLUMN IF NOT EXISTS "identity_number" VARCHAR(30);

