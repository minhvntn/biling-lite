DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typname = 'CommandType'
          AND n.nspname = 'public'
    ) THEN
        IF NOT EXISTS (
            SELECT 1
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'public'
              AND t.typname = 'CommandType'
              AND e.enumlabel = 'RESTART'
        ) THEN
            ALTER TYPE "public"."CommandType" ADD VALUE 'RESTART';
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'public'
              AND t.typname = 'CommandType'
              AND e.enumlabel = 'SHUTDOWN'
        ) THEN
            ALTER TYPE "public"."CommandType" ADD VALUE 'SHUTDOWN';
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'public'
              AND t.typname = 'CommandType'
              AND e.enumlabel = 'CLOSE_APPS'
        ) THEN
            ALTER TYPE "public"."CommandType" ADD VALUE 'CLOSE_APPS';
        END IF;
    END IF;
END $$;
