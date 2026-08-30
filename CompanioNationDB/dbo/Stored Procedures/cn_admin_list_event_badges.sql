-- Returns all event badge definitions for the admin badge editor.
CREATE PROCEDURE [dbo].[cn_admin_list_event_badges]
	@login_token UNIQUEIDENTIFIER
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;
	SELECT @caller_user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token AND is_administrator = 1;

	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 400000, 'Unauthorized. Admin access required.', 1;
	END;

	SELECT
		badge_id,
		name,
		description,
		icon
	FROM cn_event_badges
	ORDER BY name ASC;
END
GO
