CREATE PROCEDURE cn_update_review_visibility
    @login_token UNIQUEIDENTIFIER,
    @image_id INT,
    @is_public BIT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;

    -- Validate login token and retrieve user_id
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;


    -- Update the public visibility of the image review
    UPDATE cn_images
    SET review_visible = @is_public
    WHERE image_id = @image_id
    AND user_id = @user_id;

    -- Return success message
    SELECT 0 AS ErrorCode, 'Visibility updated successfully.' AS Message;
END;
