CREATE PROCEDURE [dbo].[cn_get_user_images]
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @caller_user_id INT;
    DECLARE @is_admin BIT;
    DECLARE @user_id INT;

    -- Validate login token and get caller identity
    SELECT @caller_user_id = user_id, @is_admin = is_administrator
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@caller_user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Determine which user's images to retrieve
    IF (@target_user_id IS NOT NULL AND @target_user_id != @caller_user_id)
    BEGIN
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

    -- Retrieve images associated with the user
    SELECT image_id, image_guid, connection_id, date_created, rating, review, image_visible, review_visible
    FROM cn_images
    WHERE user_id = @user_id
    ORDER BY image_id DESC;
END;
GO
