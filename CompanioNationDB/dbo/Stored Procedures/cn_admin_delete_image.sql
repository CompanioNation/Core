CREATE PROCEDURE [dbo].[cn_admin_delete_image]
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT,
    @image_id INT,
    @image_guid UNIQUEIDENTIFIER OUTPUT
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

    -- Get the image GUID
    SELECT @image_guid = image_guid
    FROM cn_images
    WHERE image_id = @image_id AND user_id = @target_user_id;

    IF (@image_guid IS NULL)
    BEGIN;
        THROW 400001, 'Photo not found.', 1;
    END;

    -- Delete the image record
    DELETE FROM cn_images
    WHERE image_id = @image_id AND user_id = @target_user_id;
END
