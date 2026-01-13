CREATE PROCEDURE [dbo].[cn_add_advice]
    @login_token UNIQUEIDENTIFIER,
    @prompt NVARCHAR(MAX),
	@advice NVARCHAR(MAX)
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

    INSERT INTO cn_advice (user_id, prompt, advice) VALUES (@user_id, @prompt, @advice)

	    
RETURN 0
