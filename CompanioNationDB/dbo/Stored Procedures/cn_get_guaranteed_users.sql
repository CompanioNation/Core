CREATE PROCEDURE [dbo].[cn_get_guaranteed_users]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;

    -- Validate the login token and get the user ID
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    -- If user is not found, throw an error
    IF @user_id IS NULL
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Fetch the list of users guaranteed by the logged-in user along with the specific image used for the guarantee
    SELECT 
        u.user_id, 
        u.email, 
        u.name,
        i.image_id,
        i.image_guid,  -- The specific image used when the guarantee was made
        i.rating,
        i.review
    FROM cn_users u
    JOIN cn_images i ON u.user_id = i.user_id
    WHERE i.guarantor_user_id = @user_id
    ORDER BY i.date_created DESC;
END;
