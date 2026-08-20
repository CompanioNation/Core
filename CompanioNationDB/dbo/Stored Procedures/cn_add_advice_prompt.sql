CREATE PROCEDURE [dbo].[cn_add_advice_prompt]
	@login_token UNIQUEIDENTIFIER,
	@thread_id INT,
	@prompt NVARCHAR(MAX)
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

	-- Seed the title from the first prompt when it is still NULL.
	UPDATE cn_advice_threads
	SET title = CASE WHEN title IS NULL THEN LEFT(@prompt, 200) ELSE title END,
		last_updated = GETUTCDATE()
	WHERE thread_id = @thread_id;

	-- Insert the prompt with a NULL response; the response is filled in by
	-- cn_set_advice_response once the model finishes streaming.
	INSERT INTO cn_advice_exchanges (thread_id, user_id, prompt, response)
	VALUES (@thread_id, @user_id, @prompt, NULL);

	SELECT SCOPE_IDENTITY() AS exchange_id;

RETURN 0
