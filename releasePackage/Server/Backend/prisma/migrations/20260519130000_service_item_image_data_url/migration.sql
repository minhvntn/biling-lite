-- AlterTable
ALTER TABLE "public"."service_items"
ADD COLUMN IF NOT EXISTS "image_data_url" TEXT;
