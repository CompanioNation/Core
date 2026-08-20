CREATE PROCEDURE [dbo].[cn_start_advice_thread]
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

	INSERT INTO cn_advice_threads (user_id)
	VALUES (@user_id);

	SELECT SCOPE_IDENTITY() AS thread_id;

RETURN 0
