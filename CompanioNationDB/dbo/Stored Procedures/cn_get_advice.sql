CREATE PROCEDURE [dbo].[cn_get_advice]
    @login_token UNIQUEIDENTIFIER
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

    SELECT * FROM cn_advice WHERE user_id = @user_id ORDER BY advice_id DESC

RETURN 0
