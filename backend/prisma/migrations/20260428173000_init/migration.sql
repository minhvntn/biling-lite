-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "public";

-- CreateEnum
CREATE TYPE "public"."PcStatus" AS ENUM ('OFFLINE', 'ONLINE', 'IN_USE', 'LOCKED');

-- CreateEnum
CREATE TYPE "public"."SessionStatus" AS ENUM ('ACTIVE', 'CLOSED');

-- CreateEnum
CREATE TYPE "public"."ClosedReason" AS ENUM ('ADMIN_LOCK', 'AUTO_OFFLINE', 'SYSTEM');

-- CreateEnum
CREATE TYPE "public"."CommandType" AS ENUM ('OPEN', 'LOCK');

-- CreateEnum
CREATE TYPE "public"."CommandStatus" AS ENUM ('PENDING', 'SENT', 'ACK_SUCCESS', 'ACK_FAILED', 'TIMEOUT');

-- CreateEnum
CREATE TYPE "public"."EventSource" AS ENUM ('ADMIN', 'SERVER', 'CLIENT');

-- CreateTable
CREATE TABLE "public"."pcs" (
    "id" UUID NOT NULL,
    "agent_id" VARCHAR(100) NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "hostname" VARCHAR(120),
    "ip_address" VARCHAR(45),
    "status" "public"."PcStatus" NOT NULL DEFAULT 'OFFLINE',
    "last_seen_at" TIMESTAMPTZ(6),
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "pcs_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."pricing_config" (
    "id" UUID NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "price_per_minute" DECIMAL(12,2) NOT NULL,
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "pricing_config_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."sessions" (
    "id" UUID NOT NULL,
    "pc_id" UUID NOT NULL,
    "started_at" TIMESTAMPTZ(6) NOT NULL,
    "ended_at" TIMESTAMPTZ(6),
    "duration_seconds" INTEGER,
    "billable_minutes" INTEGER,
    "price_per_minute" DECIMAL(12,2),
    "amount" DECIMAL(12,2),
    "status" "public"."SessionStatus" NOT NULL DEFAULT 'ACTIVE',
    "closed_reason" "public"."ClosedReason",
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "sessions_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."commands" (
    "id" UUID NOT NULL,
    "pc_id" UUID NOT NULL,
    "type" "public"."CommandType" NOT NULL,
    "status" "public"."CommandStatus" NOT NULL DEFAULT 'PENDING',
    "requested_by" VARCHAR(100) NOT NULL,
    "requested_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "sent_at" TIMESTAMPTZ(6),
    "ack_at" TIMESTAMPTZ(6),
    "error_message" TEXT,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "commands_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."events_log" (
    "id" UUID NOT NULL,
    "source" "public"."EventSource" NOT NULL,
    "event_type" VARCHAR(50) NOT NULL,
    "pc_id" UUID,
    "payload" JSONB,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "events_log_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "pcs_agent_id_key" ON "public"."pcs"("agent_id");

-- CreateIndex
CREATE INDEX "idx_pcs_status" ON "public"."pcs"("status");

-- CreateIndex
CREATE UNIQUE INDEX "pricing_config_name_key" ON "public"."pricing_config"("name");

-- CreateIndex
CREATE INDEX "idx_sessions_pc_id" ON "public"."sessions"("pc_id");

-- CreateIndex
CREATE INDEX "idx_sessions_started_at" ON "public"."sessions"("started_at");

-- CreateIndex
CREATE INDEX "idx_commands_pc_id" ON "public"."commands"("pc_id");

-- CreateIndex
CREATE INDEX "idx_commands_status" ON "public"."commands"("status");

-- AddForeignKey
ALTER TABLE "public"."sessions" ADD CONSTRAINT "sessions_pc_id_fkey" FOREIGN KEY ("pc_id") REFERENCES "public"."pcs"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."commands" ADD CONSTRAINT "commands_pc_id_fkey" FOREIGN KEY ("pc_id") REFERENCES "public"."pcs"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."events_log" ADD CONSTRAINT "events_log_pc_id_fkey" FOREIGN KEY ("pc_id") REFERENCES "public"."pcs"("id") ON DELETE SET NULL ON UPDATE CASCADE;

