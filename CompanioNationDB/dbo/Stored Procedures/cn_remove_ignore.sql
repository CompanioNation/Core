CREATE PROCEDURE [dbo].[cn_remove_ignore]
    @login_token UNIQUEIDENTIFIER,
    @user_id_to_ignore int
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

    DELETE FROM cn_ignore WHERE user_id = @user_id AND user_id_to_ignore = @user_id_to_ignore

RETURN 0
