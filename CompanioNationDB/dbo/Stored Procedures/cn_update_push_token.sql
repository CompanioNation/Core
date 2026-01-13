CREATE PROCEDURE [dbo].[cn_update_push_token]
	@login_token UNIQUEIDENTIFIER,
	@push_token NVARCHAR(1024)
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


	UPDATE cn_users SET push_token = @push_token WHERE user_id = @user_id

RETURN 0
