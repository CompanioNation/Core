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

    -- Update the user ranking
    UPDATE cn_users 
    SET ranking = COALESCE(ranking, 0) + @rating - COALESCE(cn_images.rating, 0), @target_user_id = cn_users.user_id
    FROM cn_images
    WHERE cn_images.image_id = @image_id
        AND cn_images.guarantor_user_id = @user_id
        AND cn_users.user_id = cn_images.user_id;

    IF @target_user_id IS NULL 
    BEGIN;
        THROW 50000, 'Permissions error', 1;
    END;

    UPDATE cn_users
    SET average_rating = (SELECT AVG(rating) FROM cn_images WHERE cn_images.user_id = @target_user_id)
    WHERE user_id = @target_user_id

    -- Update the rating and review for the specified image
    UPDATE cn_images
    SET rating = @rating,
        review = @review
    WHERE image_id = @image_id AND guarantor_user_id = @user_id;

    -- Return success message
    SELECT 0 AS ErrorCode, 'Review updated successfully.' AS Message;
END;
