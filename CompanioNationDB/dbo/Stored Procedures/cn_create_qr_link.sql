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

        -- Canonical ordering for duplicate prevention
        DECLARE @u1 INT = CASE WHEN @user_id < @target_user_id THEN @user_id ELSE @target_user_id END;
        DECLARE @u2 INT = CASE WHEN @user_id < @target_user_id THEN @target_user_id ELSE @user_id END;

        DECLARE @lock_result INT, @lock_name NVARCHAR(255) = CONCAT('link:', @u1, ':', @u2);
        EXEC @lock_result = sys.sp_getapplock @Resource = @lock_name, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lock_result < 0 THROW 50000, 'Unable to lock LINK pair', 1;

        -- Check for duplicate
        IF EXISTS (SELECT 1 FROM cn_connections WHERE user1 = @u1 AND user2 = @u2)
           OR EXISTS (SELECT 1 FROM cn_connections WHERE user1 = @u2 AND user2 = @u1 AND confirmed = 1)
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
