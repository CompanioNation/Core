CREATE PROCEDURE [dbo].[cn_get_advice_exchanges]
	@login_token UNIQUEIDENTIFIER,
	@thread_id INT
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

	-- Ownership check: the thread must belong to the caller
	IF NOT EXISTS (SELECT 1 FROM cn_advice_threads t WHERE t.thread_id = @thread_id AND t.user_id = @user_id)
	BEGIN;
		THROW 50003, 'Thread not found or access denied.', 1;
	END;

	SELECT e.exchange_id,
		   e.prompt,
		   e.response,
		   e.date_created
	FROM cn_advice_exchanges e
	WHERE e.thread_id = @thread_id
	ORDER BY e.exchange_id ASC;

RETURN 0
