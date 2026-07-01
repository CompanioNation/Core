CREATE PROCEDURE [dbo].[cn_get_user_by_google_token]
	@google_purchase_token NVARCHAR(512)
AS
BEGIN
	SET NOCOUNT ON;

	IF @google_purchase_token IS NULL OR @google_purchase_token = ''
		THROW 50000, 'Google purchase token is required', 1;

	SELECT 
		user_id,
		email,
		subscription_expiry,
		payment_system
	FROM cn_users 
	WHERE google_purchase_token = @google_purchase_token;
END
