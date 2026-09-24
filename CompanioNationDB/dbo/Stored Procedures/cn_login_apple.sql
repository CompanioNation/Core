-- =============================================
-- Apple Sign-In login path, keyed by the signed "sub" claim.
--
-- The id_token's "sub" claim is always present and cryptographically signed by
-- Apple, so it is the trustworthy identity anchor — even when the "email" claim
-- is absent from a response. Email is treated as profile data, not identity.
--
-- Two trust levels arrive through @email_is_trusted:
--   1 = email came from Apple's signed id_token  -> may link an existing account
--       and auto-verifies (mirrors the old cn_login OAuth behaviour).
--   0 = email came from the forgeable form_post  -> may ONLY create a brand-new
--       account; never looks up / attaches to an existing one, and never
--       auto-verifies. This is what makes the handoff safe: forging it can at
--       worst create an unverified email-squat account, never take one over.
-- =============================================
CREATE PROCEDURE [dbo].[cn_login_apple]
	@apple_sub nvarchar(255),
	@email nvarchar(255) = NULL,
	@email_is_trusted bit = 0,
	@ip_address varchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id int;

	-- 1) Resolve by the stable Apple subject identifier.
	SET @user_id = (SELECT TOP 1 user_id FROM cn_users WHERE apple_sub = @apple_sub);

	-- 2) Only a TRUSTED email may claim an existing account.
	IF @user_id IS NULL AND @email IS NOT NULL AND @email_is_trusted = 1
	BEGIN
		SET @user_id = (SELECT TOP 1 user_id FROM cn_users WHERE email = @email);
	END

	-- 3) No match -> create. cn_create_new_user throws 100005 if the email belongs
	--    to an active account, which is exactly the safe behaviour for an untrusted
	--    email (never attach to someone else's account).
	IF @user_id IS NULL
	BEGIN
		IF @email IS NULL
		BEGIN
			;THROW 100000, 'Apple sign-in did not provide an email address.', 1;
		END

		EXEC cn_create_new_user
			@email = @email,
			@password = NULL,
			@ip_address = @ip_address,
			@oauth_login = 1;

		SET @user_id = (SELECT TOP 1 user_id FROM cn_users WHERE email = @email);
	END

	-- 4) Bind the subject so future sign-ins resolve by it even if Apple omits the
	--    email claim. Never steal a subject already bound to a different account.
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE apple_sub = @apple_sub AND user_id <> @user_id)
		UPDATE cn_users SET apple_sub = @apple_sub WHERE user_id = @user_id;

	-- 5) Issue a fresh session (mirrors cn_login). Only a trusted email auto-verifies.
	DECLARE @guid uniqueidentifier = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER);

	UPDATE cn_users
	SET login_token   = @guid,
		failed_logins = 0,
		last_login    = GETUTCDATE(),
		last_login_ip = @ip_address,
		push_token    = '',
		is_deleted    = 0,
		verified      = CASE WHEN @email_is_trusted = 1 THEN 1 ELSE verified END
	WHERE user_id = @user_id;

	EXEC cn_get_user @user_id;

END
