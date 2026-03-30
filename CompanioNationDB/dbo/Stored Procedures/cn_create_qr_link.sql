CREATE PROCEDURE [dbo].[cn_create_qr_link]
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @user_id INT;

        -- Validate login token
        SELECT @user_id = user_id
        FROM cn_users
        WHERE login_token = @login_token;

        IF @user_id IS NULL
        BEGIN
            THROW 100000, 'Invalid Credentials', 1;
        END;

        -- Prevent self-link
        IF @user_id = @target_user_id
        BEGIN
            THROW 500002, 'Cannot link with yourself', 1;
        END;

        -- Verify target user exists
        IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @target_user_id)
        BEGIN
            THROW 500007, 'Target user not found', 1;
        END;

        -- Rate limit: max 20 QR links per 24 hours
        DECLARE @link_count INT;
        SELECT @link_count = COUNT(*)
        FROM cn_connections
        WHERE user1 = @user_id
          AND link_type = 0
          AND date_created >= DATEADD(HOUR, -24, GETUTCDATE());

        -- Also count where user is user2 (they scanned someone else's QR)
        SELECT @link_count = @link_count + COUNT(*)
        FROM cn_connections
        WHERE user2 = @user_id
          AND link_type = 0
          AND date_created >= DATEADD(HOUR, -24, GETUTCDATE());

        IF @link_count >= 20
        BEGIN
            THROW 500005, 'Rate limit exceeded for QR links', 1;
        END;

        -- Canonical ordering for duplicate prevention
        DECLARE @u1 INT = CASE WHEN @user_id < @target_user_id THEN @user_id ELSE @target_user_id END;
        DECLARE @u2 INT = CASE WHEN @user_id < @target_user_id THEN @target_user_id ELSE @user_id END;

        -- Check for duplicate
        IF EXISTS (SELECT 1 FROM cn_connections WHERE user1 = @u1 AND user2 = @u2)
        BEGIN
            THROW 500003, 'Link already exists', 1;
        END;

        -- Insert confirmed QR link
        INSERT INTO cn_connections (user1, user2, link_type, confirmed)
        VALUES (@u1, @u2, 0, 1);

        -- Apply +2 ranking to both users
        UPDATE cn_users
        SET ranking = ranking + 2
        WHERE user_id IN (@user_id, @target_user_id);

        -- Return the linked user details
        DECLARE @connection_id INT = (SELECT connection_id FROM cn_connections WHERE user1 = @u1 AND user2 = @u2);

        SELECT
            u.user_id AS UserId,
            u.name AS Name,
            @connection_id AS ConnectionId,
            0 AS LinkType,
            GETUTCDATE() AS DateLinked,
            ISNULL((SELECT TOP 1 image_guid FROM cn_images WHERE user_id = @target_user_id AND image_visible = 1 ORDER BY date_created DESC), CAST(0x0 AS UNIQUEIDENTIFIER)) AS Thumbnail,
            2 AS KarmaEarned
        FROM cn_users u
        WHERE u.user_id = @target_user_id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
