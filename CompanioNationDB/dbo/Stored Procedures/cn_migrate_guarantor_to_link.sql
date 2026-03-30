CREATE PROCEDURE [dbo].[cn_migrate_guarantor_to_link]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate admin access
    DECLARE @admin_user_id INT;
    DECLARE @is_admin BIT;

    SELECT @admin_user_id = user_id, @is_admin = is_administrator
    FROM cn_users
    WHERE login_token = @login_token;

    IF @admin_user_id IS NULL
    BEGIN
        THROW 100000, 'Invalid Credentials', 1;
    END

    IF @is_admin = 0
    BEGIN
        THROW 400000, 'Admin access required.', 1;
    END

    -- Check if guarantor_user_id column still exists on cn_images
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'cn_images' AND COLUMN_NAME = 'guarantor_user_id'
    )
    BEGIN
        -- Column already dropped — migration is complete
        SELECT
            0 AS TotalImages,
            0 AS Migrated,
            0 AS Orphaned,
            0 AS AlreadyMigrated;
        RETURN;
    END

    -- Count images with guarantor_user_id set
    DECLARE @totalImages INT;
    DECLARE @migrated INT = 0;
    DECLARE @orphaned INT = 0;
    DECLARE @alreadyMigrated INT = 0;

    -- Use dynamic SQL because the column may not exist in the SSDT schema
    -- but does exist in the live database during the transition period
    CREATE TABLE #migration_results (
        TotalImages INT,
        Migrated INT,
        Orphaned INT,
        AlreadyMigrated INT
    );

    EXEC sp_executesql N'
        DECLARE @total INT, @migrated INT = 0, @orphaned INT = 0, @already INT = 0;

        -- Count total images that have a guarantor_user_id
        SELECT @total = COUNT(*)
        FROM cn_images
        WHERE guarantor_user_id IS NOT NULL;

        -- Count those already migrated (connection_id already set)
        SELECT @already = COUNT(*)
        FROM cn_images
        WHERE guarantor_user_id IS NOT NULL
          AND connection_id IS NOT NULL;

        -- Migrate: set connection_id from matching cn_connections row
        -- cn_connections stores (user1, user2) in canonical order (user1 < user2)
        UPDATE i
        SET i.connection_id = c.connection_id
        FROM cn_images i
        INNER JOIN cn_connections c
            ON (
                (c.user1 = i.user_id AND c.user2 = i.guarantor_user_id)
                OR
                (c.user1 = i.guarantor_user_id AND c.user2 = i.user_id)
            )
        WHERE i.guarantor_user_id IS NOT NULL
          AND i.connection_id IS NULL;

        SET @migrated = @@ROWCOUNT;

        -- Orphans: images with guarantor_user_id but no matching connection
        -- Leave connection_id as NULL (treated as self-upload)
        SELECT @orphaned = COUNT(*)
        FROM cn_images
        WHERE guarantor_user_id IS NOT NULL
          AND connection_id IS NULL;

        INSERT INTO #migration_results (TotalImages, Migrated, Orphaned, AlreadyMigrated)
        VALUES (@total, @migrated, @orphaned, @already);
    ';

    SELECT TotalImages, Migrated, Orphaned, AlreadyMigrated
    FROM #migration_results;

    DROP TABLE #migration_results;
END
GO
