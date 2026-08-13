CREATE PROCEDURE [dbo].[cn_request_email_change]
	@login_token UNIQUEIDENTIFIER,
	@new_email NVARCHAR(255)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id INT;
	DECLARE @current_email NVARCHAR(255);
	DECLARE @oauth_login BIT;

	-- Validate the login token and load the caller's current email/login type.
	SELECT @user_id = user_id,
		   @current_email = email,
		   @oauth_login = oauth_login
	FROM cn_users
	WHERE login_token = @login_token
	  AND is_deleted = 0;

	IF (@user_id IS NULL)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	-- OAuth accounts are looked up by their provider email, so a local email
	-- change would orphan future provider logins. Their email must be changed
	-- at the provider instead.
	IF (@oauth_login = 1)
	BEGIN;
		THROW 50003, 'Email changes are managed by your sign-in provider.', 1;
	END;

	IF (NULLIF(LTRIM(RTRIM(@new_email)), '') IS NULL OR CHARINDEX('@', @new_email) = 0)
	BEGIN;
		THROW 50001, 'Invalid email format.', 1;
	END;

	SET @new_email = LTRIM(RTRIM(@new_email));

	IF (@new_email = @current_email)
	BEGIN;
		THROW 50003, 'New email address must be different from your current email.', 1;
	END;

	-- The email column has a UNIQUE constraint, so it cannot match any row
	-- (including soft-deleted accounts that still reserve their email).
	IF EXISTS (SELECT 1 FROM cn_users WHERE email = @new_email AND user_id <> @user_id)
	BEGIN;
		THROW 100005, 'An account with this email address already exists.', 1;
	END;

	-- Stage the change. old_email keeps the previous address for audit purposes;
	-- new_email holds the unverified target address until it is confirmed.
	UPDATE cn_users
	SET old_email = @current_email,
		new_email = @new_email,
		verification_code = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER),
		verification_code_timestamp = GETUTCDATE()
	WHERE user_id = @user_id;

	-- Return the code and the staged address so the API can send the email.
	SELECT verification_code, new_email
	FROM cn_users
	WHERE user_id = @user_id;
END
