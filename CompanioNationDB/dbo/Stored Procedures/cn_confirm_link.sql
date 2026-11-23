CREATE PROCEDURE [dbo].[cn_confirm_link]
    @verification_code UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @user1 INT;
        DECLARE @user2 INT;
        DECLARE @confirmed BIT;
        DECLARE @expires_at DATETIME;

        -- Look up the connection
        SELECT @user1 = user1, @user2 = user2, @confirmed = confirmed, @expires_at = expires_at
        FROM cn_connections
        WHERE verification_code = @verification_code;

        IF @user1 IS NULL
        BEGIN
            THROW 500007, 'Link invitation not found or expired', 1;
        END;

        DECLARE @u1 INT = CASE WHEN @user1 < @user2 THEN @user1 ELSE @user2 END;
        DECLARE @u2 INT = CASE WHEN @user1 < @user2 THEN @user2 ELSE @user1 END;
        DECLARE @lock_result INT, @lock_name NVARCHAR(255) = CONCAT('link:', @u1, ':', @u2);
        EXEC @lock_result = sys.sp_getapplock @Resource = @lock_name, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lock_result < 0 THROW 50000, 'Unable to lock LINK pair', 1;

        -- Re-read after acquiring the pair lock; another confirmation may have completed.
        SET @user1 = NULL;
        SELECT @user1 = user1, @user2 = user2, @confirmed = confirmed, @expires_at = expires_at
        FROM cn_connections WHERE verification_code = @verification_code;
        IF @user1 IS NULL THROW 500007, 'Link invitation not found or expired', 1;

        -- Check if already confirmed
        IF @confirmed = 1 OR EXISTS (SELECT 1 FROM cn_connections
                                     WHERE user1 = @user2 AND user2 = @user1 AND confirmed = 1)
        BEGIN
            -- Return indication that it was already confirmed
            SELECT 'already_confirmed' AS status, NULL AS login_token, NULL AS user_id, NULL AS name;
            COMMIT TRANSACTION;
            RETURN;
        END;

        -- Check expiry
        IF @expires_at IS NOT NULL AND @expires_at < GETUTCDATE()
        BEGIN
            THROW 500000, 'Link invitation has expired', 1;
        END;

        -- Confirm the link
        UPDATE cn_connections
        SET confirmed = 1
        WHERE verification_code = @verification_code;

        -- Apply +2 ranking to both users
        UPDATE cn_users
        SET ranking = ranking + 2
        WHERE user_id IN (@user1, @user2);

        -- Generate a login token for the target user (user2)
        DECLARE @login_token UNIQUEIDENTIFIER = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER);
        UPDATE cn_users
        SET login_token = @login_token, last_login = GETUTCDATE()
        WHERE user_id = @user2;

        -- Return success with login token and user details
        SELECT
            'confirmed' AS status,
            @login_token AS login_token,
            u.user_id,
            u.name,
            (SELECT name FROM cn_users WHERE user_id = @user1) AS initiator_name
        FROM cn_users u
        WHERE u.user_id = @user2;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
