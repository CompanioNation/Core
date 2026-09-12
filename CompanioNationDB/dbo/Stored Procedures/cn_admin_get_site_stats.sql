CREATE PROCEDURE [dbo].[cn_admin_get_site_stats]
	@login_token UNIQUEIDENTIFIER
AS
BEGIN
	SET NOCOUNT ON;

	-- Validate admin
	DECLARE @user_id INT;
	SELECT @user_id = user_id FROM cn_users WHERE login_token = @login_token AND is_administrator = 1;
	IF @user_id IS NULL
	BEGIN; THROW 400000, 'Unauthorized', 1; END;

	-- The figures themselves live in cn_get_site_stats, which is shared with the server-side
	-- nightly maintenance report so both always show identical numbers. This wrapper exists
	-- solely to gate the client-reachable path on admin credentials.
	EXEC [dbo].[cn_get_site_stats];
END
RETURN 0
