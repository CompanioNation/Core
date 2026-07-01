CREATE PROCEDURE [dbo].[cn_get_user_by_microsoft_transaction]
	@microsoft_transaction_id NVARCHAR(255)
AS
BEGIN
	SET NOCOUNT ON;

	IF @microsoft_transaction_id IS NULL OR @microsoft_transaction_id = ''
		THROW 50000, 'Microsoft transaction ID is required', 1;

	SELECT 
		user_id,
		email,
		subscription_expiry,
		payment_system
	FROM cn_users 
	WHERE microsoft_transaction_id = @microsoft_transaction_id;
END
