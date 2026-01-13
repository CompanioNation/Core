CREATE PROCEDURE cn_update_user_info
    @login_token UNIQUEIDENTIFIER,
    @name NVARCHAR(50),
    @description NVARCHAR(MAX),
    @searchable BIT,
    @gender INT,
    @geonameid INT,
    @dob DATETIME
AS
BEGIN
    DECLARE @user_id INT;

    -- Validate login token and retrieve user_id
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Update user details including gender
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
