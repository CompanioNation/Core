CREATE PROCEDURE cn_update_user_info
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT = NULL,
    @name NVARCHAR(50),
    @description NVARCHAR(MAX),
    @searchable BIT,
    @gender INT,
    @geonameid INT,
    @dob DATETIME
AS
BEGIN
    DECLARE @caller_user_id INT;
    DECLARE @is_admin BIT;
    DECLARE @user_id INT;

    -- Validate login token and retrieve caller identity
    SELECT @caller_user_id = user_id, @is_admin = is_administrator
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@caller_user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Determine which user to update
    IF (@target_user_id IS NOT NULL AND @target_user_id != @caller_user_id)
    BEGIN
        -- Trying to update another user - must be admin
        IF (@is_admin = 0)
        BEGIN;
            THROW 400000, 'Unauthorized. Admin access required.', 1;
        END;
        SET @user_id = @target_user_id;
    END
    ELSE
    BEGIN
        SET @user_id = @caller_user_id;
    END;

    -- Update user details
    UPDATE cn_users
    SET 
        name = @name,
        description = @description,
        searchable = @searchable,
        gender = @gender,
        geonameid = @geonameid,
        bday = @dob
    WHERE user_id = @user_id;

END
