CREATE PROCEDURE [dbo].[cn_admin_dismiss_profile]
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @caller_user_id INT;
    DECLARE @is_admin BIT;

    -- Validate login token and verify admin
    SELECT @caller_user_id = user_id, @is_admin = is_administrator
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@caller_user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    IF (@is_admin = 0)
    BEGIN;
        THROW 400000, 'Unauthorized. Admin access required.', 1;
    END;

    -- Boost ranking to move profile down the triage queue
    UPDATE cn_users SET ranking = ranking + 1
    WHERE user_id = @target_user_id;

    IF (@@ROWCOUNT = 0)
    BEGIN;
        THROW 400001, 'Profile not found.', 1;
    END;
END
