CREATE PROCEDURE cn_delete_profile
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    DECLARE @user_id INT;

    -- Validate login token
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Hide all user images
    UPDATE cn_images
    SET image_visible = 0
    WHERE user_id = @user_id;

    -- Clear personal profile fields, hide from search, and invalidate login
    UPDATE cn_users
    SET searchable     = 0,
        name           = 'Deleted User',
        description    = '',
        login_token    = NULL,
        push_token     = '',
        main_photo_id  = NULL
    WHERE user_id = @user_id;
END
