CREATE PROCEDURE [dbo].[cn_set_advice_response]
	@login_token UNIQUEIDENTIFIER,
	@thread_id INT,
	@exchange_id INT,
	@response NVARCHAR(MAX)
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

	-- Ownership-checked update: only the thread owner may fill the response.
	UPDATE e
	SET e.response = @response
	FROM cn_advice_exchanges e
	INNER JOIN cn_advice_threads t ON t.thread_id = e.thread_id
	WHERE e.exchange_id = @exchange_id
	  AND e.thread_id = @thread_id
	  AND t.user_id = @user_id;

	IF @@ROWCOUNT = 0
	BEGIN;
		THROW 50003, 'Exchange not found or access denied.', 1;
	END;

	UPDATE cn_advice_threads
	SET last_updated = GETUTCDATE()
	WHERE thread_id = @thread_id;

	SELECT @exchange_id AS exchange_id;

RETURN 0
