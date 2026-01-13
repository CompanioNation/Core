CREATE PROCEDURE cn_remove_guarantee
    @login_token UNIQUEIDENTIFIER,
    @image_id INT,
    @image_guid UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    -- Validate the login token and permissions here
    DECLARE @user_id INT;
    
    -- Check if the user exists
    SELECT @user_id = user_id 
    FROM cn_users 
    WHERE login_token = @login_token;

    IF (@user_id IS NULL) 
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Retrieve the image GUID before deletion
    DECLARE @target_user_id INT;
    DECLARE @ranking_change INT;
    SELECT @image_guid = image_guid, @target_user_id = cn_images.user_id, @ranking_change = COALESCE(cn_images.rating, 0)
    FROM cn_images
    WHERE image_id = @image_id
        AND guarantor_user_id = @user_id;

    IF (@image_guid IS NULL)
    BEGIN;
        THROW 100002, 'Image does not exist.', 1;
    END;


    -- Update the ranking of the guarantor user and the target user
    UPDATE cn_users 
    SET ranking = COALESCE(ranking, 0) - 1 
    WHERE user_id = @user_id OR user_id = @target_user_id;

    -- Also remove the additional ranking value that was included as part of the review
    UPDATE cn_users
    SET ranking = COALESCE(ranking, 0) - @ranking_change
    WHERE user_id = @target_user_id;


    -- Attempt to remove the guarantee from cn_images
    DECLARE @rows_affected INT;

    DELETE FROM cn_images 
    WHERE image_id = @image_id;

    SET @rows_affected = @@ROWCOUNT;

    -- Check if the delete operation was successful
    IF (@rows_affected = 0)
    BEGIN;
        THROW 100001, 'You do not have permission to remove this guarantee or it does not exist.', 1;
    END;

END
GO
