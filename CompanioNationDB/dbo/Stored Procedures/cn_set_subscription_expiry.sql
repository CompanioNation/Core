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

	UPDATE cn_users 
	SET subscription_expiry = @expiry_date
	WHERE email = @email;

	SELECT @@ROWCOUNT AS rows_affected;
END
GO
