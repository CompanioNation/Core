CREATE PROCEDURE [dbo].[cn_get_suburbs]
    @login_token UNIQUEIDENTIFIER
AS

    DECLARE @user_id INT;

    -- Validate the login token and get the user ID
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF @user_id IS NULL
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;


	SELECT * FROM cn_suburbs ORDER BY order_index;

RETURN 0
