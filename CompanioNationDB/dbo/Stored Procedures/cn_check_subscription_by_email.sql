-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Check if an email has an active subscription
-- Returns 1 if active, 0 if not
-- Used for pre-login subscription checks
-- =============================================
CREATE PROCEDURE [dbo].[cn_check_subscription_by_email]
	@email NVARCHAR(255)
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
	WHERE email = @email;
END
GO
