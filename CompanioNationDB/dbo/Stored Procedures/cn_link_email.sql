CREATE PROCEDURE [dbo].[cn_link_email]
    @login_token UNIQUEIDENTIFIER,
    @email VARCHAR(1024)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @user_id INT;
        DECLARE @link_complaints INT;

        -- Validate login token
        SELECT @user_id = user_id, @link_complaints = link_complaints
        FROM cn_users
        WHERE login_token = @login_token;

        IF @user_id IS NULL
        BEGIN
            THROW 100000, 'Invalid Credentials', 1;
        END;

        -- Check complaint threshold
        IF @link_complaints >= 3
        BEGIN
            THROW 500006, 'Email linking blocked due to complaints', 1;
        END;

        -- Rate limit: max 5 email links per 24 hours
        DECLARE @email_count INT;
        SELECT @email_count = COUNT(*)
        FROM cn_connections
        WHERE user1 = @user_id
          AND link_type = 1
          AND date_created >= DATEADD(HOUR, -24, GETUTCDATE());

        IF @email_count >= 5
        BEGIN
            THROW 500005, 'Rate limit exceeded for email links', 1;
        END;

        -- Validate email format
        IF CHARINDEX('@', @email) = 0
        BEGIN
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
        BEGIN
            THROW 500002, 'Cannot link with yourself', 1;
        END;

        -- Canonical ordering
        DECLARE @u1 INT = CASE WHEN @user_id < @target_user_id THEN @user_id ELSE @target_user_id END;
        DECLARE @u2 INT = CASE WHEN @user_id < @target_user_id THEN @target_user_id ELSE @user_id END;

        -- Check for duplicate
        IF EXISTS (SELECT 1 FROM cn_connections WHERE user1 = @u1 AND user2 = @u2)
        BEGIN
            -- Already exists, return NULL verification code (no email needed)
            SELECT NULL AS verification_code;
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
