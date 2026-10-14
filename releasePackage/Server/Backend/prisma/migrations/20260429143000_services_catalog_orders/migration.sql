-- CreateTable
CREATE TABLE IF NOT EXISTS "public"."service_items" (
    "id" UUID NOT NULL,
    "name" VARCHAR(120) NOT NULL,
    "category" VARCHAR(80),
    "unit_price" DECIMAL(12,2) NOT NULL,
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "service_items_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX IF NOT EXISTS "service_items_name_key" ON "public"."service_items"("name");
CREATE INDEX IF NOT EXISTS "idx_service_items_is_active" ON "public"."service_items"("is_active");

-- CreateTable
CREATE TABLE IF NOT EXISTS "public"."pc_service_orders" (
    "id" UUID NOT NULL,
    "pc_id" UUID NOT NULL,
    "session_id" UUID,
    "service_item_id" UUID NOT NULL,
    "quantity" INTEGER NOT NULL DEFAULT 1,
    "unit_price" DECIMAL(12,2) NOT NULL,
    "line_total" DECIMAL(12,2) NOT NULL,
    "note" VARCHAR(255),
    "created_by" VARCHAR(100) NOT NULL,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "pc_service_orders_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX IF NOT EXISTS "idx_pc_service_orders_pc_id" ON "public"."pc_service_orders"("pc_id");
CREATE INDEX IF NOT EXISTS "idx_pc_service_orders_session_id" ON "public"."pc_service_orders"("session_id");
CREATE INDEX IF NOT EXISTS "idx_pc_service_orders_item_id" ON "public"."pc_service_orders"("service_item_id");
CREATE INDEX IF NOT EXISTS "idx_pc_service_orders_created_at" ON "public"."pc_service_orders"("created_at");

-- AddForeignKey
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'pc_service_orders_pc_id_fkey'
  ) THEN
    ALTER TABLE "public"."pc_service_orders"
      ADD CONSTRAINT "pc_service_orders_pc_id_fkey"
      FOREIGN KEY ("pc_id") REFERENCES "public"."pcs"("id")
      ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'pc_service_orders_session_id_fkey'
  ) THEN
    ALTER TABLE "public"."pc_service_orders"
      ADD CONSTRAINT "pc_service_orders_session_id_fkey"
      FOREIGN KEY ("session_id") REFERENCES "public"."sessions"("id")
      ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'pc_service_orders_service_item_id_fkey'
  ) THEN
    ALTER TABLE "public"."pc_service_orders"
      ADD CONSTRAINT "pc_service_orders_service_item_id_fkey"
      FOREIGN KEY ("service_item_id") REFERENCES "public"."service_items"("id")
      ON DELETE RESTRICT ON UPDATE CASCADE;
  END IF;
END $$;

