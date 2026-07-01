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
		THROW 50000, 'Email is required', 1;

	IF @google_purchase_token IS NULL OR @google_purchase_token = ''
		THROW 50000, 'Google purchase token is required', 1;

	IF @payment_system IS NULL OR @payment_system = ''
		THROW 50000, 'Payment system is required', 1;

	-- Create user if they don't exist
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE email = @email)
	BEGIN
		EXEC cn_create_new_user @email, NULL;
	END

	-- Update subscription expiry, payment system, and Google purchase token
	UPDATE cn_users 
	SET 
		subscription_expiry = @expiry_date,
		payment_system = @payment_system,
		google_purchase_token = @google_purchase_token
	WHERE email = @email;

	IF @@ROWCOUNT = 0
		THROW 50000, 'User not found or update failed', 1;
END
