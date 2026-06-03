-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Set subscription expiry date directly by email.
--              This is used by payment provider webhooks to sync the expiry
--              date directly from provider subscription data.
-- =============================================
CREATE PROCEDURE [dbo].[cn_set_subscription_expiry]
	@email NVARCHAR(255),
	@expiry_date DATETIME,
	@payment_system NVARCHAR(50) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- If the user does not exist, create them just like we do in the oauth login in [cn_login]
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE email = @email)
	BEGIN
		EXEC cn_create_new_user 
			@email = @email,
			@password = '',
			@ip_address = '0.0.0.0',
			@oauth_login = 1;
	END

	-- Update subscription expiry and optionally payment system
	UPDATE cn_users 
	SET 
		subscription_expiry = @expiry_date,
		payment_system = COALESCE(@payment_system, payment_system)
	WHERE email = @email;

	SELECT @@ROWCOUNT AS rows_affected;
END
GO
