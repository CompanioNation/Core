CREATE PROCEDURE [dbo].[cn_get_user_images]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;

    -- Validate login token and get user_id
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Retrieve images associated with the user
    SELECT image_id, image_guid, guarantor_user_id, date_created, rating, review, image_visible, review_visible
    FROM cn_images
    WHERE user_id = @user_id
    ORDER BY image_id DESC; -- order by id rather than date_created because it is indexed and proportional
END;
GO
