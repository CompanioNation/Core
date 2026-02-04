-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Update subscription expiry date for a user by user_id
-- Used by Stripe webhook to set subscription end date
-- =============================================
CREATE PROCEDURE [dbo].[cn_update_subscription_expiry_by_userid]
	@user_id INT,
	@expiry_date DATETIME
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE cn_users 
	SET subscription_expiry = @expiry_date
	WHERE user_id = @user_id;

	-- Return affected rows for logging
	SELECT @@ROWCOUNT AS rows_affected;
END
GO
