CREATE PROCEDURE cn_update_image_review
    @login_token UNIQUEIDENTIFIER,
    @image_id INT,
    @rating INT,
    @review NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

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


    DECLARE @target_user_id INT;

    -- Validate that the caller is the photographer (connected user who is NOT the subject)
    -- For LINK photos: connection_id IS NOT NULL, photographer is the other user in the connection
    -- For non-LINK photos: connection_id IS NULL, the photo owner can review their own photos
    DECLARE @image_owner INT;
    DECLARE @conn_id INT;

    SELECT @image_owner = i.user_id, @conn_id = i.connection_id
    FROM cn_images i
    WHERE i.image_id = @image_id;

    IF @image_owner IS NULL
    BEGIN;
        THROW 50000, 'Image not found', 1;
    END;

    -- For LINK photos, verify the caller is part of the connection and is the photographer (not the subject)
    IF @conn_id IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM cn_connections c
            WHERE c.connection_id = @conn_id
              AND (c.user1 = @user_id OR c.user2 = @user_id)
              AND @image_owner != @user_id  -- caller must NOT be the subject
        )
        BEGIN;
            THROW 50000, 'Permissions error', 1;
        END;
        SET @target_user_id = @image_owner;
    END
    ELSE
    BEGIN
        -- Non-LINK photo: no permission model for reviews on self-uploads
        SET @target_user_id = @image_owner;
    END;

    -- Update the user ranking
    UPDATE cn_users 
    SET ranking = COALESCE(ranking, 0) + @rating - COALESCE((SELECT rating FROM cn_images WHERE image_id = @image_id), 0)
    WHERE user_id = @target_user_id;

    UPDATE cn_users
    SET average_rating = (SELECT AVG(rating) FROM cn_images WHERE cn_images.user_id = @target_user_id)
    WHERE user_id = @target_user_id

    -- Update the rating and review for the specified image
    UPDATE cn_images
    SET rating = @rating,
        review = @review
    WHERE image_id = @image_id;

    -- Return success message
    SELECT 0 AS ErrorCode, 'Review updated successfully.' AS Message;
END;
