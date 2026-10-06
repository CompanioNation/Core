-- =============================================
-- Single universal login path.
-- Password verification happens in C# (PBKDF2) before this procedure is called
-- for password logins. OAuth logins create the account here when it doesn't
-- exist yet. Every successful login reactivates a previously deleted account
-- and issues a fresh session token.
-- =============================================
CREATE PROCEDURE [dbo].[cn_login]
	@email varchar(1024),
	@ip_address varchar(50),
	@oauth_login bit = 0,
	-- 1 when this call CREATED a brand-new account or REACTIVATED a previously
	-- deleted one. Callers use this to send a welcome email without guessing from
	-- timestamps. 0 for an ordinary login to an existing active account.
	@is_new_account BIT = 0 OUTPUT
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id int;
	DECLARE @was_deleted bit = 0;

	SELECT @user_id = user_id, @was_deleted = is_deleted
	FROM cn_users WHERE email = @email;

	-- OAuth sign-in creates the account on first use.
	IF @oauth_login = 1 AND @user_id IS NULL
	BEGIN
		DECLARE @created bit = 0;

		EXEC cn_create_new_user
			@email = @email,
			@password = NULL,
			@ip_address = @ip_address,
			@oauth_login = 1,
			@is_new_account = @created OUTPUT;

		SET @user_id = (SELECT user_id FROM cn_users WHERE email = @email);
		SET @is_new_account = @created;
	END
	ELSE IF @was_deleted = 1
	BEGIN
		-- An existing soft-deleted row is being reactivated — treat it as new.
		SET @is_new_account = 1;
	END

	-- Password logins reach this point only after C# has verified the password,
	-- so a missing user here is always an invalid-credentials condition.
	IF @user_id IS NULL
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END

	DECLARE @guid uniqueidentifier = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER)

	-- A fresh login invalidates any prior session on another device, so clear
	-- push_token; the newly logged-in device re-uploads its own token via
	-- cn_update_push_token as soon as its client resubscribes. OAuth logins are
	-- always email-verified. A successful login always reactivates a previously
	-- deleted account.
	UPDATE cn_users
	SET login_token   = @guid,
		failed_logins = 0,
		last_login    = GETUTCDATE(),
		last_login_ip = @ip_address,
		push_token    = '',
		is_deleted    = 0,
		verified      = CASE WHEN @oauth_login = 1 THEN 1 ELSE verified END,
		date_created  = CASE WHEN @was_deleted = 1 THEN GETUTCDATE() ELSE date_created END,
		emails_enabled = CASE WHEN @was_deleted = 1 THEN 1 ELSE emails_enabled END
	WHERE user_id = @user_id;

	-- Return the user details
	EXEC cn_get_user @user_id

END

