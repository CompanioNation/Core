CREATE PROCEDURE cn_update_image_visibility
    @login_token UNIQUEIDENTIFIER,
    @image_id INT,
    @is_visible BIT
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

    UPDATE cn_images
    SET image_visible = @is_visible
    WHERE image_id = @image_id
    AND user_id = @user_id;

END
