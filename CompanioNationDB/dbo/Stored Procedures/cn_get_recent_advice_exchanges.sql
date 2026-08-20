CREATE PROCEDURE [dbo].[cn_get_recent_advice_exchanges]
	@login_token UNIQUEIDENTIFIER,
	@thread_id INT,
	@count INT = 10
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

	-- Recent exchanges (each a self-contained prompt + response pair) from the user's
	-- OTHER advice threads, most recent first, used as past context for the prompt builder.
	SELECT TOP (@count) e.exchange_id,
		   e.prompt,
		   e.response,
		   e.date_created
	FROM cn_advice_exchanges e
	WHERE e.user_id = @user_id
	  AND e.thread_id <> @thread_id
	ORDER BY e.exchange_id DESC;

RETURN 0
