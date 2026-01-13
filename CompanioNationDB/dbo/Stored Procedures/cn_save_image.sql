CREATE PROCEDURE [dbo].[cn_save_image]
    @login_token UNIQUEIDENTIFIER,
	@image_guid UNIQUEIDENTIFIER,
    @ip_address nvarchar(50)
AS

    DECLARE @user_id INT;

    -- Validate the login token against the users table
    SELECT @user_id = user_id 
    FROM cn_users 
    WHERE login_token = @login_token;

    -- Throw error if credentials are invalid
    IF (@user_id IS NULL) 
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

	INSERT INTO cn_images (user_id, image_guid, ip_address) VALUES (@user_id, @image_guid, @ip_address)

RETURN 0
