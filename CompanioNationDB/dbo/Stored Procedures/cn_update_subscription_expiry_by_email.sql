-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Update subscription expiry date for a user by email
-- Used by Stripe webhook to set subscription end date
-- =============================================
CREATE PROCEDURE [dbo].[cn_update_subscription_expiry_by_email]
	@email NVARCHAR(255),
	@expiry_date DATETIME
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE cn_users 
	SET subscription_expiry = @expiry_date
	WHERE email = @email;

	-- Return affected rows for logging
	SELECT @@ROWCOUNT AS rows_affected;
END
GO
