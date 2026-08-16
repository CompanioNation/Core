-- =============================================
-- Author:      <Author,,Name>
-- Create date: <Create Date,,>
-- Description: Validates the login token and updates the last login timestamp
-- =============================================
CREATE PROCEDURE cn_validate_login_token
	@login_token VARCHAR(1024)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id INT;

	-- Look up the user without writing.
	SELECT @user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token;

	-- If no rows were returned, raise an error for token timeout or invalid token
	IF @user_id IS NULL
	BEGIN;
		THROW 100000, 'Login token has expired or is invalid.', 1;
	END

	-- Only refresh last_login at most once every 5 minutes. This procedure runs on
	-- every authenticated hub call, so an unconditional write would hammer the row
	-- and inflate IO. "Active user" stats keep 5-minute granularity, which does not
	-- change the daily/7-day/30-day buckets in practice.
	UPDATE cn_users
	SET last_login = GETUTCDATE()
	WHERE user_id = @user_id
	  AND (last_login IS NULL OR DATEDIFF(MINUTE, last_login, GETUTCDATE()) >= 5);

	-- Return the user details
	EXEC cn_get_user @user_id

END
