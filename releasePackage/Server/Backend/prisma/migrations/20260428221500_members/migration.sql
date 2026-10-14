-- CreateEnum
CREATE TYPE "public"."MemberTransactionType" AS ENUM ('TOPUP', 'BUY_PLAYTIME', 'ADJUSTMENT');

-- CreateTable
CREATE TABLE "public"."members" (
    "id" UUID NOT NULL,
    "username" VARCHAR(50) NOT NULL,
    "full_name" VARCHAR(120) NOT NULL,
    "phone" VARCHAR(30),
    "balance" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "play_seconds" INTEGER NOT NULL DEFAULT 0,
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "members_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."member_transactions" (
    "id" UUID NOT NULL,
    "member_id" UUID NOT NULL,
    "type" "public"."MemberTransactionType" NOT NULL,
    "amount_delta" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "play_seconds_delta" INTEGER NOT NULL DEFAULT 0,
    "note" TEXT,
    "created_by" VARCHAR(100) NOT NULL,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "member_transactions_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "members_username_key" ON "public"."members"("username");

-- CreateIndex
CREATE INDEX "idx_members_full_name" ON "public"."members"("full_name");

-- CreateIndex
CREATE INDEX "idx_member_transactions_member_id" ON "public"."member_transactions"("member_id");

-- CreateIndex
CREATE INDEX "idx_member_transactions_created_at" ON "public"."member_transactions"("created_at");

-- AddForeignKey
ALTER TABLE "public"."member_transactions" ADD CONSTRAINT "member_transactions_member_id_fkey" FOREIGN KEY ("member_id") REFERENCES "public"."members"("id") ON DELETE CASCADE ON UPDATE CASCADE;
