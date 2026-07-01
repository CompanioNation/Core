CREATE PROCEDURE [dbo].[cn_set_microsoft_subscription]
	@email NVARCHAR(255),
	@expiry_date DATETIME,
	@microsoft_transaction_id NVARCHAR(255),
	@payment_system NVARCHAR(50)
AS
BEGIN
	SET NOCOUNT ON;

	-- Validate parameters
	IF @email IS NULL OR @email = ''
		THROW 50000, 'Email is required', 1;

	IF @microsoft_transaction_id IS NULL OR @microsoft_transaction_id = ''
		THROW 50000, 'Microsoft transaction ID is required', 1;

	IF @payment_system IS NULL OR @payment_system = ''
		THROW 50000, 'Payment system is required', 1;

	-- Create user if they don't exist
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE email = @email)
	BEGIN
		EXEC cn_create_new_user @email, NULL;
	END

	-- Update subscription expiry, payment system, and Microsoft transaction ID
	UPDATE cn_users 
	SET 
		subscription_expiry = @expiry_date,
		payment_system = @payment_system,
		microsoft_transaction_id = @microsoft_transaction_id
	WHERE email = @email;

	IF @@ROWCOUNT = 0
		THROW 50000, 'User not found or update failed', 1;
END
