CREATE PROCEDURE [dbo].[cn_get_advice_threads]
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

	SELECT t.thread_id,
		   t.title,
		   t.date_created,
		   t.last_updated,
		   (SELECT COUNT(*) FROM cn_advice_exchanges e WHERE e.thread_id = t.thread_id) AS exchange_count,
		   (SELECT TOP 1 e.prompt FROM cn_advice_exchanges e WHERE e.thread_id = t.thread_id ORDER BY e.exchange_id DESC) AS last_prompt
	FROM cn_advice_threads t
	WHERE t.user_id = @user_id
	ORDER BY t.last_updated DESC;

RETURN 0
