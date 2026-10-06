-- =============================================
-- Sets whether a user may receive CompanioNation email.
-- Used by the unsubscribe deep link / Gmail one-click endpoint.
--
-- Keyed by email address so an unsubscribe link works without a login token.
-- A no-op for addresses that match no user (must never error).
-- =============================================
CREATE PROCEDURE [dbo].[cn_set_email_enabled]
	@email          NVARCHAR(255),
	@emails_enabled BIT
AS
BEGIN
	SET NOCOUNT ON;

	IF @email IS NULL OR LTRIM(RTRIM(@email)) = ''
		RETURN;

	UPDATE cn_users
	SET emails_enabled = @emails_enabled
	WHERE email = LTRIM(RTRIM(@email));
END;
