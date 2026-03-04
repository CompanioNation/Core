-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Set subscription expiry date directly by email.
--              This is used by Stripe webhooks to sync the expiry
--              date directly from Stripe's current_period_end.
-- =============================================
CREATE PROCEDURE [dbo].[cn_set_subscription_expiry]
	@email NVARCHAR(255),
	@expiry_date DATETIME
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

	UPDATE cn_users 
	SET subscription_expiry = @expiry_date
	WHERE email = @email;

	SELECT @@ROWCOUNT AS rows_affected;
END
GO
