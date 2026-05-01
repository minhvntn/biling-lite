-- CreateTable
CREATE TABLE IF NOT EXISTS "public"."pc_groups" (
    "id" UUID NOT NULL,
    "name" VARCHAR(80) NOT NULL,
    "hourly_rate" DECIMAL(12,2) NOT NULL,
    "is_default" BOOLEAN NOT NULL DEFAULT false,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "pc_groups_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX IF NOT EXISTS "pc_groups_name_key" ON "public"."pc_groups"("name");
CREATE INDEX IF NOT EXISTS "idx_pc_groups_is_default" ON "public"."pc_groups"("is_default");

-- AlterTable
ALTER TABLE "public"."pcs" ADD COLUMN IF NOT EXISTS "group_id" UUID;

-- CreateIndex
CREATE INDEX IF NOT EXISTS "idx_pcs_group_id" ON "public"."pcs"("group_id");

-- AddForeignKey
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'pcs_group_id_fkey'
  ) THEN
    ALTER TABLE "public"."pcs"
      ADD CONSTRAINT "pcs_group_id_fkey"
      FOREIGN KEY ("group_id") REFERENCES "public"."pc_groups"("id")
      ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END $$;

-- Seed default group and backfill existing PCs
INSERT INTO "public"."pc_groups" ("id", "name", "hourly_rate", "is_default", "created_at", "updated_at")
SELECT gen_random_uuid(), 'Mặc định', 5000, true, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
WHERE NOT EXISTS (
  SELECT 1 FROM "public"."pc_groups" WHERE "is_default" = true
);

UPDATE "public"."pcs"
SET "group_id" = (
  SELECT "id"
  FROM "public"."pc_groups"
  WHERE "is_default" = true
  ORDER BY "updated_at" DESC
  LIMIT 1
)
WHERE "group_id" IS NULL;

