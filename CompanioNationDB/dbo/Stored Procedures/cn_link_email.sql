CREATE PROCEDURE [dbo].[cn_link_email]
    @login_token UNIQUEIDENTIFIER,
    @email VARCHAR(1024)
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
        BEGIN;
            THROW 100000, 'Invalid Credentials', 1;
        END;

        -- TODO: Revisit whether rejected email invitations should ever block new invitations.
        -- DECLARE @link_complaints INT;
        -- SELECT @link_complaints = link_complaints FROM cn_users WHERE user_id = @user_id;
        -- IF @link_complaints >= 3
        -- BEGIN;
        --     THROW 500006, 'Email linking blocked due to complaints', 1;
        -- END;

        -- Validate email format
        IF CHARINDEX('@', @email) = 0
        BEGIN;
            THROW 50001, 'Invalid email format', 1;
        END;

        DECLARE @target_user_id INT;
        DECLARE @name NVARCHAR(50);

        -- Extract name from email prefix
        SET @name = SUBSTRING(@email, 1, CHARINDEX('@', @email) - 1);

        -- Check if target user exists
        SELECT @target_user_id = user_id FROM cn_users WHERE email = @email;

        IF @target_user_id IS NULL
        BEGIN
            -- Create new user
            INSERT INTO cn_users (email, name, verification_code, searchable, gender)
            VALUES (@email, @name, CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER), 1, 1);

            SET @target_user_id = SCOPE_IDENTITY();
        END;

        -- Prevent self-link
        IF @user_id = @target_user_id
        BEGIN;
            THROW 500002, 'Cannot link with yourself', 1;
        END;

        -- Canonical ordering
        DECLARE @u1 INT = CASE WHEN @user_id < @target_user_id THEN @user_id ELSE @target_user_id END;
        DECLARE @u2 INT = CASE WHEN @user_id < @target_user_id THEN @target_user_id ELSE @user_id END;

        -- All LINK creation and confirmation paths serialize on the same unordered pair.
        DECLARE @lock_result INT, @lock_name NVARCHAR(255) = CONCAT('link:', @u1, ':', @u2);
        EXEC @lock_result = sys.sp_getapplock @Resource = @lock_name, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lock_result < 0 THROW 50000, 'Unable to lock LINK pair', 1;

        -- A confirmed row in EITHER direction means both parties already accepted.
        IF EXISTS (SELECT 1 FROM cn_connections
                   WHERE confirmed = 1
                     AND ((user1 = @u1 AND user2 = @u2) OR (user1 = @u2 AND user2 = @u1)))
        BEGIN
            -- Already a real LINK, no invite needed
            SELECT NULL AS verification_code;
            COMMIT TRANSACTION;
            RETURN;
        END;

        -- An unanswered invite may be re-sent, but ONLY the one this user sent: cn_confirm_link
        -- logs in user2, so re-using a reverse-direction row's code would email a code that logs
        -- the recipient in as the sender.
        DECLARE @pending_code UNIQUEIDENTIFIER = NULL;
        SELECT @pending_code = verification_code
        FROM cn_connections
        WHERE user1 = @user_id AND user2 = @target_user_id AND confirmed = 0;

        IF @pending_code IS NOT NULL
        BEGIN
            UPDATE cn_connections
            SET link_type = 1, expires_at = DATEADD(DAY, 3, GETUTCDATE())
            WHERE user1 = @user_id AND user2 = @target_user_id;

            SELECT @pending_code AS verification_code;
            COMMIT TRANSACTION;
            RETURN;
        END;

        -- Insert unconfirmed email link with expiry
        INSERT INTO cn_connections (user1, user2, link_type, confirmed, expires_at)
        VALUES (@user_id, @target_user_id, 1, 0, DATEADD(DAY, 3, GETUTCDATE()));

        -- Note: user1 is the initiator (not canonical) for email links so we can track who sent it
        -- Return verification code
        SELECT verification_code
        FROM cn_connections
        WHERE user1 = @user_id AND user2 = @target_user_id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
