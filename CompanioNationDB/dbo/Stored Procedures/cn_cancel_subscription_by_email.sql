-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Cancel subscription by clearing expiry date
-- Used by Stripe webhook when subscription is deleted
-- =============================================
CREATE PROCEDURE [dbo].[cn_cancel_subscription_by_email]
	@email NVARCHAR(255)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE cn_users 
	SET subscription_expiry = NULL
	WHERE email = @email;

	-- Return affected rows for logging
	SELECT @@ROWCOUNT AS rows_affected;
END
GO
