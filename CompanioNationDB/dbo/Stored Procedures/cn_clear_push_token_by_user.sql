-- =============================================
-- Clears the push token for a specific user.
-- Server-internal maintenance procedure: called only from server-side code
-- after a push delivery failure (stale token cleanup). It is deliberately
-- NOT exposed through any hub method, so it does not accept a login token.
-- =============================================
CREATE PROCEDURE [dbo].[cn_clear_push_token_by_user]
	@user_id INT
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE cn_users
	SET push_token = ''
	WHERE user_id = @user_id;
END;
