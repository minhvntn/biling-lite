-- CreateTable
CREATE TABLE IF NOT EXISTS "public"."member_daily_checkins" (
  "id" UUID NOT NULL,
  "member_id" UUID NOT NULL,
  "checkin_date" DATE NOT NULL,
  "created_by" VARCHAR(100) NOT NULL,
  "note" VARCHAR(255),
  "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "member_daily_checkins_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX IF NOT EXISTS "idx_member_daily_checkins_member_id"
ON "public"."member_daily_checkins"("member_id");

-- CreateIndex
CREATE INDEX IF NOT EXISTS "idx_member_daily_checkins_checkin_date"
ON "public"."member_daily_checkins"("checkin_date");

-- CreateIndex
CREATE UNIQUE INDEX IF NOT EXISTS "uq_member_daily_checkins_member_date"
ON "public"."member_daily_checkins"("member_id", "checkin_date");

-- AddForeignKey
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'member_daily_checkins_member_id_fkey'
  ) THEN
    ALTER TABLE "public"."member_daily_checkins"
    ADD CONSTRAINT "member_daily_checkins_member_id_fkey"
    FOREIGN KEY ("member_id") REFERENCES "public"."members"("id")
    ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;
END $$;
