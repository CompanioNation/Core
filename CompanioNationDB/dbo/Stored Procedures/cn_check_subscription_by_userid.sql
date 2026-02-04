-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Check if a user has an active subscription
-- Returns 1 if active, 0 if not
-- =============================================
CREATE PROCEDURE [dbo].[cn_check_subscription_by_userid]
	@user_id INT
AS
BEGIN
	SET NOCOUNT ON;

	SELECT CASE 
		WHEN subscription_expiry IS NOT NULL 
			AND subscription_expiry > GETUTCDATE() 
		THEN 1 
		ELSE 0 
	END AS has_active_subscription
	FROM cn_users 
	WHERE user_id = @user_id;
END
GO
