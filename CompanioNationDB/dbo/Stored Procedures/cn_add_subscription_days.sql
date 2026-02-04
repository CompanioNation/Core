-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2025
-- Description:	Add or subtract days from subscription expiry by email.
--              If current expiry is NULL or in the past, starts from today.
--              Positive days = extend, Negative days = reduce (e.g., chargebacks)
-- =============================================
CREATE PROCEDURE [dbo].[cn_add_subscription_days]
	@email NVARCHAR(255),
	@days_to_add INT
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @today DATE = CAST(GETUTCDATE() AS DATE);

	UPDATE cn_users 
	SET subscription_expiry = DATEADD(DAY, @days_to_add, 
		CASE 
			WHEN subscription_expiry IS NULL OR subscription_expiry < @today 
			THEN @today 
			ELSE subscription_expiry 
		END)
	WHERE email = @email;

	SELECT @@ROWCOUNT AS rows_affected;
END
GO
