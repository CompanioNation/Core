CREATE PROCEDURE [dbo].[cn_set_google_subscription]
	@email NVARCHAR(255),
	@expiry_date DATETIME,
	@google_purchase_token NVARCHAR(512),
	@payment_system NVARCHAR(50)
AS
BEGIN
	SET NOCOUNT ON;

	-- Validate parameters
	IF @email IS NULL OR @email = ''
		THROW 50001, 'Email is required', 1;

	IF @google_purchase_token IS NULL OR @google_purchase_token = ''
		THROW 50001, 'Google purchase token is required', 1;

	IF @payment_system IS NULL OR @payment_system = ''
		THROW 50001, 'Payment system is required', 1;

	-- Create user if they don't exist.
	-- Named parameters are REQUIRED: cn_create_new_user's leading parameters are
	-- @N/@T, so positional arguments would bind the email to @N and fail.
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE email = @email)
	BEGIN
		EXEC cn_create_new_user
			@email = @email,
			@password = '',
			@ip_address = '0.0.0.0',
			@oauth_login = 1;
	END

	-- Capture the PREVIOUS expiry so the caller can tell a real change from a replay.
	DECLARE @previous_expiry DATETIME;
	SELECT @previous_expiry = subscription_expiry FROM cn_users WHERE email = @email;

	-- Update subscription expiry, payment system, and Google purchase token
	UPDATE cn_users 
	SET 
		subscription_expiry = @expiry_date,
		payment_system = @payment_system,
		google_purchase_token = @google_purchase_token
	WHERE email = @email;

	IF @@ROWCOUNT = 0
		THROW 50002, 'User not found or update failed', 1;

	SELECT @previous_expiry AS previous_expiry;
END
